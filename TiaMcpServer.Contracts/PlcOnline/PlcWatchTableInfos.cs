using System.Collections;
using System.Globalization;
using System.Reflection;

namespace TiaMcpServer.Contracts.PlcOnline;

/// <summary><c>list_watch_and_force_tables</c>: every matching PLC's watch-and-force table groups and tables.</summary>
public class PlcWatchTableInventoryInfo
{
    public List<PlcWatchTablePlcInfo> Plcs { get; set; } = new List<PlcWatchTablePlcInfo>();
}

public class PlcWatchTablePlcInfo
{
    public string PlcName { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    /// <summary>User group paths ("/Line", "/Line/Cell"); the system group is "/" and not listed.</summary>
    public List<string> Groups { get; set; } = new List<string>();

    public List<PlcWatchTableSummaryInfo> WatchTables { get; set; } = new List<PlcWatchTableSummaryInfo>();

    public List<PlcWatchTableSummaryInfo> ForceTables { get; set; } = new List<PlcWatchTableSummaryInfo>();

    /// <summary>False when the table cap stopped the walk.</summary>
    public bool Complete { get; set; } = true;
}

public class PlcWatchTableSummaryInfo
{
    /// <summary>"/" for the system group, otherwise the user group path.</summary>
    public string GroupPath { get; set; } = "/";

    public string Name { get; set; } = string.Empty;

    /// <summary><c>IsConsistent</c>; null when unreadable.</summary>
    public bool? IsConsistent { get; set; }
}

/// <summary><c>read_watch_table</c>: one watch table with its entries.</summary>
public class PlcWatchTableReadInfo
{
    public string PlcName { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    public string GroupPath { get; set; } = "/";

    public string Name { get; set; } = string.Empty;

    public bool? IsConsistent { get; set; }

    /// <summary>Entries read (at most the cap).</summary>
    public int EntryCount { get; set; }

    public bool EntriesComplete { get; set; } = true;

    public List<PlcWatchTableEntryInfo> Entries { get; set; } = new List<PlcWatchTableEntryInfo>();
}

/// <summary>One <c>PlcWatchTableEntry</c>, read through <c>GetAttribute</c> by the documented V21 member names.</summary>
public class PlcWatchTableEntryInfo
{
    public int Index { get; set; }

    public string? Name { get; set; }

    public string? Address { get; set; }

    public string? DisplayFormat { get; set; }

    public string? MonitorTrigger { get; set; }

    public bool? ModifyIntention { get; set; }

    public string? ModifyTrigger { get; set; }

    public string? ModifyValue { get; set; }

    /// <summary>Attributes that threw or returned an unexpected type; their members are null.</summary>
    public List<string> UnreadableAttributes { get; set; } = new List<string>();
}

/// <summary><c>read_force_table</c>: one force table with its entries.</summary>
public class PlcForceTableReadInfo
{
    public string PlcName { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    public string GroupPath { get; set; } = "/";

    public string Name { get; set; } = string.Empty;

    public bool? IsConsistent { get; set; }

    public int EntryCount { get; set; }

    public bool EntriesComplete { get; set; } = true;

    public List<PlcForceTableEntryInfo> Entries { get; set; } = new List<PlcForceTableEntryInfo>();
}

/// <summary>One <c>PlcForceTableEntry</c>, read through <c>GetAttribute</c> by the documented V21 member names.</summary>
public class PlcForceTableEntryInfo
{
    public int Index { get; set; }

    public string? Name { get; set; }

    public string? Address { get; set; }

    public string? DisplayFormat { get; set; }

    public string? MonitorTrigger { get; set; }

    public bool? ForceIntention { get; set; }

    public string? ForceValue { get; set; }

    public List<string> UnreadableAttributes { get; set; } = new List<string>();
}

/// <summary>Result of a watch/force table write and of update_program.</summary>
public class PlcWatchTableMutationResultInfo
{
    public string Operation { get; set; } = string.Empty;

    public string PlcName { get; set; } = string.Empty;

    /// <summary>The group the operation acted in (or, for a group operation, the group itself).</summary>
    public string GroupPath { get; set; } = "/";

    /// <summary>The table name; null for group operations and update_program.</summary>
    public string? TableName { get; set; }

    /// <summary>The names <c>Import</c> returned (content updates); empty otherwise.</summary>
    public List<string> ImportedTables { get; set; } = new List<string>();

    /// <summary>update_program only: the runtime type of what <c>UpdateProgram()</c> returned, or null (void or null).</summary>
    public string? ReturnedType { get; set; }

    /// <summary>
    /// update_program only: <see cref="PlcUpdateProgramOutcome.Succeeded"/> (a returned state, error-count or success member
    /// says so) or <see cref="PlcUpdateProgramOutcome.Uninterpreted"/> (nothing to interpret; the call raised no error, but
    /// its outcome is not verified). A failing member fails the call instead. Null for every other operation.
    /// </summary>
    public string? Outcome { get; set; }

    /// <summary>update_program only: the returned object's public readable scalar members (at most 20); empty otherwise.</summary>
    public List<PlcEngineeringAttributeInfo> ReturnedMembers { get; set; } = new List<PlcEngineeringAttributeInfo>();
}

/// <summary>Siemens-free projection rules of watch/force tables, shared by worker, FakeWorker and tests.</summary>
public static class PlcWatchTablePolicy
{
    /// <summary>
    /// The check before a SimaticML import with <c>ImportOptions.Override</c>: the document must name exactly the
    /// addressed table, so an import can never replace or add another table. The sources document no SimaticML
    /// schema for watch/force tables, so the expected shape is taken from <paramref name="exported"/>, the
    /// worker's own export of the addressed table: its root element, its one table element (a root child with
    /// <c>AttributeList/Name</c>) and that name. The import must have the same root, exactly one table element
    /// of the same element name, and the same name (ordinal). An export whose shape cannot be recognised refuses
    /// the import (fail closed). DTDs and external resources are never processed. Returns the refusal, or null.
    /// </summary>
    public static string? CheckImportDocument(string? exported, string? import, string tableName)
    {
        if (!TryParse(exported, out var export) || export!.Root is null)
            return $"the current export of '{tableName}' could not be parsed, so the import document cannot be checked";
        var exportTables = TableElements(export.Root).ToList();
        if (exportTables.Count != 1 || !string.Equals(TableName(exportTables[0]), tableName, StringComparison.Ordinal))
            return $"the current export of '{tableName}' has no single recognisable table element (AttributeList/Name '{tableName}'), "
                + "so the import document cannot be checked";
        if (!TryParse(import, out var document) || document!.Root is null)
            return "content is not a well-formed XML document (DTDs are not accepted)";
        if (document.Root.Name.LocalName != export.Root.Name.LocalName)
            return $"content's root element is '{document.Root.Name.LocalName}', but a SimaticML export of this table has '{export.Root.Name.LocalName}'";
        var tables = TableElements(document.Root).ToList();
        if (tables.Count != 1)
            return $"content must describe exactly one table, but it has {tables.Count} table element(s)";
        if (tables[0].Name.LocalName != exportTables[0].Name.LocalName)
            return $"content describes a '{tables[0].Name.LocalName}', but '{tableName}' is a '{exportTables[0].Name.LocalName}'";
        var named = TableName(tables[0]);
        return string.Equals(named, tableName, StringComparison.Ordinal)
            ? null
            : $"content names table '{PlcOnlinePolicy.BoundText(named)}', not the addressed table '{tableName}'";
    }

    private static bool TryParse(string? text, out System.Xml.Linq.XDocument? document)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = System.Xml.XmlReader.Create(new System.IO.StringReader(text!), settings);
            document = System.Xml.Linq.XDocument.Load(reader);
            return true;
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }

    private static IEnumerable<System.Xml.Linq.XElement> TableElements(System.Xml.Linq.XElement root)
        => root.Elements().Where(element => element.Elements().Any(child => child.Name.LocalName == "AttributeList"
            && child.Elements().Any(name => name.Name.LocalName == "Name")));

    private static string TableName(System.Xml.Linq.XElement table)
        => table.Elements().First(child => child.Name.LocalName == "AttributeList")
            .Elements().First(name => name.Name.LocalName == "Name").Value.Trim();

    public const int DefaultMaxEntries = 1000;

    public const int MaxEntries = 5000;

    public const int MaxTables = 5000;

    /// <summary>The documented V21 property name of the entry composition on both table types.</summary>
    public const string EntriesPropertyName = "Entries";

    public static IReadOnlyList<string> WatchEntryAttributes { get; } = new[]
    {
        "Name", "Address", "DisplayFormat", "MonitorTrigger", "ModifyIntention", "ModifyTrigger", "ModifyValue",
    };

    public static IReadOnlyList<string> ForceEntryAttributes { get; } = new[]
    {
        "Name", "Address", "DisplayFormat", "MonitorTrigger", "ForceIntention", "ForceValue",
    };

    /// <summary>Normalizes a group path: "/" for the system group, "/A/B" otherwise.</summary>
    public static string NormalizeGroupPath(string? path)
    {
        var segments = Segments(path);
        return segments.Count == 0 ? "/" : "/" + string.Join("/", segments);
    }

    public static IReadOnlyList<string> Segments(string? path)
        => (path ?? string.Empty).Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim())
            .Where(s => s.Length > 0).ToArray();

    /// <summary>
    /// Enumerates a table's <c>Entries</c> composition by reflection on the runtime proxy type (the V21
    /// reference names the property but documents no composition type; the Siemens object-tree-walking
    /// guidance discovers composition properties this way). Null when the property is missing or not enumerable.
    /// </summary>
    public static IEnumerable<object>? Entries(object table)
    {
        var property = table.GetType().GetProperty(EntriesPropertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property is null || property.GetIndexParameters().Length != 0) return null;
        object? value;
        try
        {
            value = property.GetValue(table, null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }

        return value is IEnumerable enumerable ? enumerable.Cast<object>() : null;
    }

    /// <summary>Reads one entry's documented attributes through <paramref name="getAttribute"/> (null on failure).</summary>
    public static PlcWatchTableEntryInfo WatchEntry(int index, Func<string, (bool Ok, object? Value)> getAttribute)
    {
        var entry = new PlcWatchTableEntryInfo { Index = index };
        entry.Name = Text(getAttribute, "Name", entry.UnreadableAttributes);
        entry.Address = Text(getAttribute, "Address", entry.UnreadableAttributes);
        entry.DisplayFormat = Text(getAttribute, "DisplayFormat", entry.UnreadableAttributes);
        entry.MonitorTrigger = Text(getAttribute, "MonitorTrigger", entry.UnreadableAttributes);
        entry.ModifyIntention = Flag(getAttribute, "ModifyIntention", entry.UnreadableAttributes);
        entry.ModifyTrigger = Text(getAttribute, "ModifyTrigger", entry.UnreadableAttributes);
        entry.ModifyValue = Text(getAttribute, "ModifyValue", entry.UnreadableAttributes);
        return entry;
    }

    public static PlcForceTableEntryInfo ForceEntry(int index, Func<string, (bool Ok, object? Value)> getAttribute)
    {
        var entry = new PlcForceTableEntryInfo { Index = index };
        entry.Name = Text(getAttribute, "Name", entry.UnreadableAttributes);
        entry.Address = Text(getAttribute, "Address", entry.UnreadableAttributes);
        entry.DisplayFormat = Text(getAttribute, "DisplayFormat", entry.UnreadableAttributes);
        entry.MonitorTrigger = Text(getAttribute, "MonitorTrigger", entry.UnreadableAttributes);
        entry.ForceIntention = Flag(getAttribute, "ForceIntention", entry.UnreadableAttributes);
        entry.ForceValue = Text(getAttribute, "ForceValue", entry.UnreadableAttributes);
        return entry;
    }

    /// <summary>A scalar attribute value as invariant text: strings as is, enums by name, other primitives invariantly.</summary>
    public static string? ScalarText(object? value) => value switch
    {
        null => null,
        string text => PlcOnlinePolicy.BoundText(text),
        Enum member => member.ToString(),
        bool flag => flag ? "true" : "false",
        IFormattable formattable when value.GetType().IsPrimitive || value is decimal || value is DateTime
            => PlcOnlinePolicy.BoundText(formattable.ToString(null, CultureInfo.InvariantCulture)),
        _ => null,
    };

    public static bool IsScalar(object? value)
        => value is null or string or Enum or bool or decimal or DateTime || value.GetType().IsPrimitive;

    private static string? Text(Func<string, (bool Ok, object? Value)> read, string name, List<string> unreadable)
    {
        var (ok, value) = read(name);
        if (!ok || !IsScalar(value))
        {
            unreadable.Add(name);
            return null;
        }

        return ScalarText(value);
    }

    private static bool? Flag(Func<string, (bool Ok, object? Value)> read, string name, List<string> unreadable)
    {
        var (ok, value) = read(name);
        if (ok && value is bool flag) return flag;
        if (!ok || value is not null) unreadable.Add(name);
        return null;
    }
}

/// <summary>
/// Projects result messages without a compiled schema, as the Siemens object-tree-walking guidance
/// describes: readable scalar attributes through the generic attribute API, nested messages through
/// enumerable engineering-object properties found by reflection (back-references skipped).
/// </summary>
public static class PlcEngineeringMessageProjector
{
    public const int MaxDepth = 8;

    public const int MaxMessages = 500;

    public const int MaxAttributes = 20;

    private static readonly HashSet<string> SkippedProperties = new(StringComparer.Ordinal) { "Parent", "OwnedBy" };

    /// <param name="attributeNames">Attribute names of a message (the worker reads <c>GetAttributeInfos()</c>).</param>
    /// <param name="getAttribute">One attribute's value; failure is (false, null).</param>
    /// <param name="isEngineeringObject">True for a nested item that is an engineering object (the worker checks <c>IEngineeringObject</c>).</param>
    public static (List<PlcEngineeringMessageInfo> Messages, bool Complete, string? Omission) Project(IEnumerable<object>? messages,
        Func<object, IEnumerable<string>> attributeNames, Func<object, string, (bool Ok, object? Value)> getAttribute,
        Func<object, bool> isEngineeringObject, Func<Exception, bool>? isFatal = null)
    {
        var state = new State { IsFatal = isFatal ?? (_ => false) };
        var projected = Level(messages, 1, state, attributeNames, getAttribute, isEngineeringObject);
        return (projected, state.Omission is null, state.Omission);
    }

    private sealed class State
    {
        public int Count;
        public string? Omission;
        public Func<Exception, bool> IsFatal = _ => false;
    }

    private static List<PlcEngineeringMessageInfo> Level(IEnumerable<object>? messages, int depth, State state,
        Func<object, IEnumerable<string>> attributeNames, Func<object, string, (bool Ok, object? Value)> getAttribute,
        Func<object, bool> isEngineeringObject)
    {
        var list = new List<PlcEngineeringMessageInfo>();
        if (messages is null) return list;
        foreach (var message in messages)
        {
            if (message is null) continue;
            if (state.Count >= MaxMessages)
            {
                state.Omission = $"More than {MaxMessages} messages; the rest were omitted.";
                break;
            }

            state.Count++;
            var info = new PlcEngineeringMessageInfo { Type = message.GetType().FullName ?? message.GetType().Name };
            IEnumerable<string> names;
            try
            {
                names = attributeNames(message).ToArray();
            }
            catch (Exception ex) when (!state.IsFatal(ex))
            {
                names = Array.Empty<string>();
            }

            foreach (var name in names.Take(MaxAttributes))
            {
                var (ok, value) = getAttribute(message, name);
                if (ok && value is not null && PlcWatchTablePolicy.IsScalar(value))
                    info.Attributes.Add(new PlcEngineeringAttributeInfo { Name = name, Value = PlcWatchTablePolicy.ScalarText(value) });
            }

            foreach (var children in Compositions(message, isEngineeringObject, state.IsFatal))
            {
                if (depth >= MaxDepth)
                {
                    state.Omission ??= $"Messages deeper than {MaxDepth} levels were omitted.";
                    break;
                }

                info.Messages.AddRange(Level(children, depth + 1, state, attributeNames, getAttribute, isEngineeringObject));
            }

            list.Add(info);
        }

        return list;
    }

    private static IEnumerable<IEnumerable<object>> Compositions(object message, Func<object, bool> isEngineeringObject, Func<Exception, bool> isFatal)
    {
        foreach (var property in message.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (SkippedProperties.Contains(property.Name) || property.GetIndexParameters().Length != 0
                || property.PropertyType == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(property.PropertyType))
                continue;
            object? value;
            try
            {
                value = property.GetValue(message, null);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is { } inner && isFatal(inner))
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(inner).Throw();
                throw;
            }
            catch (Exception ex) when (!isFatal(ex))
            {
                continue;
            }

            if (value is IEnumerable items)
            {
                var children = items.Cast<object>().Where(item => item is not null && isEngineeringObject(item)).ToArray();
                if (children.Length > 0) yield return children;
            }
        }
    }
}
