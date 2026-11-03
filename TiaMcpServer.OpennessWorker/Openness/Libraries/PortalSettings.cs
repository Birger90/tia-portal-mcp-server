using System.Globalization;
using System.Reflection;
using Siemens.Engineering;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Libraries;

/// <summary>
/// The general TIA Portal settings (issue #27, not live-tested). V21 documents <c>TiaPortal.SettingsFolders</c>
/// (<c>TiaPortalSettingsFolderComposition</c> with <c>Find(String)</c>), <c>TiaPortalSettingsFolder.Name</c>, <c>.Folders</c>,
/// <c>.Settings</c> (<c>TiaPortalSettingComposition</c> with <c>Find(String)</c>) and <c>TiaPortalSetting.Name</c>, <c>.Value</c>; no setting is
/// named and no value type is documented. Every member is read at runtime by its documented name (no stub member); a value is
/// written only through the live <c>Value</c> property's public setter, converted to the runtime type of the current value, and read
/// back. The settings act on the whole TIA Portal instance, not on the project.
/// </summary>
internal static class PortalSettings
{
    public static PortalSettingsInfo Read(TiaPortal portal, LibraryRequestInfo request)
    {
        var start = Segments(request.SettingsFolderPath);
        var result = new PortalSettingsInfo { FolderPath = string.Join("/", start), MaxDepth = request.MaxDepth };
        var entries = 0;
        bool Full() => entries >= LibraryPolicy.MaxTreeEntries;
        if (start.Count == 0)
        {
            foreach (var root in Folders(Root(portal), result.Unreadable)) Walk(root, new List<string>(), 1);
        }
        else Walk(ResolveFolder(portal, start), start.ToList(), 1, includeSelf: false);
        result.Unreadable = result.Unreadable.Distinct(StringComparer.Ordinal).ToList();
        return result;

        void Walk(object folder, List<string> parent, int depth, bool includeSelf = true)
        {
            if (Full()) { Incomplete($"the walk stopped at {LibraryPolicy.MaxTreeEntries} entries"); return; }
            var path = parent;
            if (includeSelf)
            {
                var name = Text(folder, "Name", result.Unreadable, "TiaPortalSettingsFolder.Name");
                if (name is null) return;
                path = parent.Concat(new[] { name }).ToList();
                result.Folders.Add(string.Join("/", path));
                entries++;
            }

            if (depth > request.MaxDepth) { Incomplete($"maxDepth {request.MaxDepth} cut the walk below {string.Join("/", path)}"); return; }
            foreach (var setting in Items(folder, "Settings", result.Unreadable, "TiaPortalSettingsFolder.Settings"))
            {
                if (Full()) { Incomplete($"the walk stopped at {LibraryPolicy.MaxTreeEntries} entries"); return; }
                result.Settings.Add(Describe(setting, string.Join("/", path)));
                entries++;
            }

            foreach (var child in Folders(folder, result.Unreadable)) Walk(child, path, depth + 1);
        }

        void Incomplete(string reason)
        {
            if (!result.Complete) return;
            result.Complete = false;
            result.Omission = reason;
        }
    }

    /// <summary>
    /// The setting as TIA Portal resolves it: the folder path and the setting name are the <c>Name</c>s of the objects
    /// <c>Find(String)</c> returned, not the caller's text, and the classification is made from them (an unreadable name makes
    /// it security-relevant; security review F2).
    /// </summary>
    public static PortalSettingInfo Current(TiaPortal portal, LibraryRequestInfo request)
    {
        var (setting, resolvedPath, unreadableName) = Resolve(portal, request);
        var info = Describe(setting, resolvedPath);
        info.SecurityRelevant = LibraryPolicy.IsSecurityRelevantResolved(info.FolderPath, info.Name, unreadableName);
        return info;
    }

    /// <summary>The setting object, the resolved folder path and whether a folder name could not be read.</summary>
    private static (object Setting, string FolderPath, bool UnreadableName) Resolve(TiaPortal portal, LibraryRequestInfo request)
    {
        var segments = Segments(request.SettingsFolderPath);
        var folders = ResolveFolders(portal, segments);
        var unreadable = new List<string>();
        var names = folders.Select((folder, index) => Text(folder, "Name", unreadable, "TiaPortalSettingsFolder.Name") ?? segments[index]).ToList();
        return (FindSetting(folders[folders.Count - 1], request.SettingName!), string.Join("/", names), unreadable.Count > 0);
    }

    public static PortalSettingChangeInfo Write(TiaPortal portal, LibraryRequestInfo request)
    {
        var (setting, path, unreadableName) = Resolve(portal, request);
        var nameRead = new List<string>();
        var resolvedName = Text(setting, "Name", nameRead, "TiaPortalSetting.Name");
        var securityRelevant = LibraryPolicy.IsSecurityRelevantResolved(path, resolvedName, unreadableName || nameRead.Count > 0);
        // The human confirmed the plan's classification; a setting that resolves differently now is not changed (F2).
        if (LibraryPolicy.PortalSettingClassificationMismatch(request.PlannedSecurityRelevant, securityRelevant, $"TIA Portal setting '{path}/{resolvedName ?? request.SettingName}'") is { } mismatch)
            throw new WorkerOperationException(WorkerFailureCategories.StateChanged, mismatch);
        var property = ValueProperty(setting);
        var current = property.GetValue(setting);
        var type = current?.GetType() ?? property.PropertyType;
        var value = Convert(request.SettingValue, type);
        var change = new PortalSettingChangeInfo
        {
            FolderPath = path, Name = resolvedName ?? request.SettingName!, Before = Display(current), ValueType = type.FullName,
            SecurityRelevant = securityRelevant,
        };
        try
        {
            property.SetValue(setting, value);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }

        var after = property.GetValue(setting);
        change.After = Display(after);
        if (!Equals(after, value) && change.After != Display(value))
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"TiaPortalSetting '{path}/{request.SettingName}' reads {change.After ?? "null"} after it was set to {Display(value)}.");
        return change;
    }

    // ---------------------------------------------------------------- navigation

    private static object Root(TiaPortal portal)
        => Required(portal, "SettingsFolders", "TiaPortal.SettingsFolders");

    public static object ResolveFolder(TiaPortal portal, IReadOnlyList<string> segments)
        => ResolveFolders(portal, segments)[segments.Count - 1];

    /// <summary>Every folder found along <paramref name="segments"/>, outermost first.</summary>
    private static IReadOnlyList<object> ResolveFolders(TiaPortal portal, IReadOnlyList<string> segments)
    {
        if (segments.Count == 0) throw LibraryResolver.Validation("settingsFolderPath must name a settings folder.");
        var folders = new List<object> { FindIn(Root(portal), segments[0], segments, 1) };
        for (var index = 1; index < segments.Count; index++)
            folders.Add(FindIn(Required(folders[index - 1], "Folders", "TiaPortalSettingsFolder.Folders"), segments[index], segments, index + 1));
        return folders;
    }

    /// <summary>The documented <c>Find(String)</c> of a settings folder composition.</summary>
    private static object FindIn(object composition, string name, IReadOnlyList<string> segments, int count)
        => LibraryReflection.Invoke(composition, "Find", new[] { typeof(string) }, new object[] { name }, "TiaPortalSettingsFolderComposition.Find(String)")
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"TIA Portal settings folder '{string.Join("/", segments.Take(count))}' was not found.");

    private static object FindSetting(object folder, string name)
        => LibraryReflection.Invoke(Required(folder, "Settings", "TiaPortalSettingsFolder.Settings"), "Find", new[] { typeof(string) }, new object[] { name },
                "TiaPortalSettingComposition.Find(String)")
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"TIA Portal setting '{name}' was not found in the folder.");

    private static IEnumerable<object> Folders(object folderOrRoot, List<string> unreadable)
    {
        var composition = folderOrRoot.GetType().GetProperty("Folders") is not null
            ? LibraryReflection.Optional(folderOrRoot, "Folders", unreadable, "TiaPortalSettingsFolder.Folders")
            : folderOrRoot;
        return composition is null ? Array.Empty<object>() : LibraryReflection.Enumerate(composition, "settings folders").ToArray();
    }

    private static IEnumerable<object> Items(object target, string name, List<string> unreadable, string documented)
    {
        var collection = LibraryReflection.Optional(target, name, unreadable, documented);
        return collection is null ? Array.Empty<object>() : LibraryReflection.Enumerate(collection, documented).ToArray();
    }

    private static PortalSettingInfo Describe(object setting, string folderPath)
    {
        var info = new PortalSettingInfo { FolderPath = folderPath };
        info.Name = Text(setting, "Name", info.Unreadable, "TiaPortalSetting.Name");
        var property = setting.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
        info.Writable = property is { CanWrite: true } && property.GetSetMethod() is not null;
        var value = LibraryReflection.Optional(setting, "Value", info.Unreadable, "TiaPortalSetting.Value");
        info.Value = Display(value);
        info.ValueType = value?.GetType().FullName;
        if (value is Enum) info.EnumMembers = Enum.GetNames(value.GetType()).ToList();
        info.SecurityRelevant = LibraryPolicy.IsSecurityRelevantSetting(folderPath, info.Name);
        return info;
    }

    private static PropertyInfo ValueProperty(object setting)
    {
        var property = setting.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
        if (property is null || !property.CanRead || property.GetSetMethod() is null)
            throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
                $"This TIA Portal installation exposes no public setter of TiaPortalSetting.Value on '{setting.GetType().FullName}'. Nothing was changed.");
        return property;
    }

    /// <summary>The caller text as the runtime type of the current value: bool, an integer, a real, an enum member by exact name, or text.</summary>
    public static object? Convert(string? text, Type type)
    {
        if (text is null) return null;
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        try
        {
            if (underlying == typeof(string)) return text;
            if (underlying == typeof(bool)) return text switch { "true" => true, "false" => false, _ => throw new FormatException("expected true or false") };
            if (underlying.IsEnum)
                return Enum.GetNames(underlying).Contains(text, StringComparer.Ordinal) ? Enum.Parse(underlying, text)
                    : throw new FormatException($"expected one of {string.Join(", ", Enum.GetNames(underlying))}");
            if (underlying.IsPrimitive || underlying == typeof(decimal))
                return System.Convert.ChangeType(text, underlying, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or InvalidCastException)
        {
            throw LibraryResolver.Validation($"settingValue '{text}' is no {underlying.Name}: {exception.Message}. Nothing was changed.");
        }

        throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
            $"The setting's value is a {underlying.FullName}; only text, true/false, numbers and enum members can be written. Nothing was changed.");
    }

    private static object Required(object target, string name, string documented)
        => LibraryReflection.Required(target, name, documented)
            ?? throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability, $"{documented} returned nothing.");

    private static string? Text(object target, string name, List<string> unreadable, string documented)
        => LibraryReflection.Display(LibraryReflection.Optional(target, name, unreadable, documented));

    public static string? Display(object? value) => value switch
    {
        null => null,
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => LibraryReflection.Display(value),
    };

    public static IReadOnlyList<string> Segments(string? path)
        => string.IsNullOrWhiteSpace(path) ? Array.Empty<string>()
            : path!.Trim('/').Split('/').Select(segment => segment.Trim()).Where(segment => segment.Length > 0).ToArray();
}
