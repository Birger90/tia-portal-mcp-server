using System.Collections;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.AddIn;
using Siemens.Engineering.AddIn.Menu;
using TiaMcpServer.Contracts.Shared.ClaudeInbox;

// "Claude in TIA" (not live-tested). Compiled against the V21 Add-In API: ContextMenuAddIn, ContextMenuAddInRoot.Items,
// ChildItemFactory.AddActionItem<T>(string, ActionItem<T>.OnClickDelegate), the four *AddInProvider base classes,
// TiaPortal (IEngineeringServiceProvider).GetService<MessageBoxProvider|FeedbackProvider>(), IEngineeringObject.GetAttribute,
// IEngineeringInstance.Parent, ProjectBase.Path. Bound at runtime by name because the V21 XML shows no return type:
// MenuSelectionProvider.GetSelection(), MessageBoxProvider.ShowNotification(NotificationIcon, string, string, string),
// FeedbackProvider.Log(NotificationIcon, string), GlobalLibrary.Path. Provenance: docs/SupportedOperations/CLAUDE_IN_TIA.md.

namespace TiaMcpServer.AddIn;

public sealed class ProjectTreeProvider : ProjectTreeAddInProvider
{
    private readonly TiaPortal _portal;

    /// <summary>TIA Portal creates the provider with its TiaPortal instance (assumed from the V21 help; not live-tested).</summary>
    public ProjectTreeProvider(TiaPortal tiaPortal) => _portal = tiaPortal;

    protected override IEnumerable<ContextMenuAddIn> GetContextMenuAddIns()
    {
        yield return new ClaudeContextMenu(_portal, ClaudeSources.ProjectTree);
    }
}

public sealed class DevicesAndNetworksProvider : DevicesAndNetworksAddInProvider
{
    private readonly TiaPortal _portal;

    public DevicesAndNetworksProvider(TiaPortal tiaPortal) => _portal = tiaPortal;

    protected override IEnumerable<ContextMenuAddIn> GetContextMenuAddIns()
    {
        yield return new ClaudeContextMenu(_portal, ClaudeSources.DevicesAndNetworks);
    }
}

public sealed class ProjectLibraryProvider : ProjectLibraryTreeAddInProvider
{
    private readonly TiaPortal _portal;

    public ProjectLibraryProvider(TiaPortal tiaPortal) => _portal = tiaPortal;

    protected override IEnumerable<ContextMenuAddIn> GetContextMenuAddIns()
    {
        yield return new ClaudeContextMenu(_portal, ClaudeSources.ProjectLibrary);
    }
}

public sealed class GlobalLibraryProvider : GlobalLibraryTreeAddInProvider
{
    private readonly TiaPortal _portal;

    public GlobalLibraryProvider(TiaPortal tiaPortal) => _portal = tiaPortal;

    protected override IEnumerable<ContextMenuAddIn> GetContextMenuAddIns()
    {
        yield return new ClaudeContextMenu(_portal, ClaudeSources.GlobalLibrary);
    }
}

/// <summary>The context menu "Claude": the five hand-over actions and "Claude-Antworten anzeigen".</summary>
public sealed class ClaudeContextMenu : ContextMenuAddIn
{
    public const string MenuName = "Claude";
    public const string ShowRepliesText = "Claude-Antworten anzeigen";
    private const string Title = "Claude in TIA";
    private const int MaxRepliesPerClick = 5;

    private readonly TiaPortal _portal;
    private readonly string _source;

    public ClaudeContextMenu(TiaPortal portal, string source) : base(MenuName)
    {
        _portal = portal;
        _source = source;
    }

    protected override void BuildContextMenuItems(ContextMenuAddInRoot addInRootSubmenu)
    {
        foreach (var action in ClaudeActions.All)
        {
            var current = action;
            addInRootSubmenu.Items.AddActionItem<IEngineeringObject>(ClaudeActions.MenuText(current), selection => Guarded(() => HandOver(current, selection)));
        }

        addInRootSubmenu.Items.AddActionItem<IEngineeringObject>(ShowRepliesText, _ => Guarded(ShowReplies));
    }

    private void HandOver(string action, MenuSelectionProvider<IEngineeringObject> selection)
    {
        var objects = SelectionReader.Selected(selection).Take(ClaudeInboxContract.MaxSelectionItems + 1).ToList();
        if (objects.Count == 0)
        {
            Notify("Warning", "Nichts ausgewählt", "Bitte zuerst ein oder mehrere Objekte auswählen.", null);
            return;
        }

        if (objects.Count > ClaudeInboxContract.MaxSelectionItems)
        {
            Notify("Warning", "Zu viele Objekte", $"Höchstens {ClaudeInboxContract.MaxSelectionItems} Objekte auf einmal übergeben.", null);
            return;
        }

        string? projectPath = null;
        var items = new List<ClaudeSelectionItem>();
        foreach (var engineeringObject in objects)
        {
            var (chain, project, library) = SelectionReader.Chain(engineeringObject);
            projectPath ??= project;
            items.Add(ClaudeSelectionAddressing.Describe(chain, library));
        }

        projectPath ??= SelectionReader.FirstProjectPath(_portal);
        var now = DateTime.UtcNow;
        var request = new ClaudeRequest(ClaudeInboxContract.NewId(now, new Random()), ClaudeInboxContract.FormatTime(now), _source, action, null,
            projectPath, items, typeof(ClaudeContextMenu).Assembly.GetName().Version?.ToString());
        Store().WriteRequest(request);
        var names = string.Join(", ", items.Select(i => i.Name ?? i.Kind).Take(5)) + (items.Count > 5 ? ", …" : "");
        Log("Information", $"Claude: Anfrage {request.Id} ({ClaudeActions.MenuText(action)}) für {names} abgelegt.");
        Notify("Information", "An Claude übergeben",
            $"{ClaudeActions.MenuText(action)}: {names}",
            $"Anfrage {request.Id} liegt im Eingang. Sag Claude „schau in TIA“ – ist die Claude-Brücke eingeschaltet, startet Claude von selbst. "
            + $"Die Antwort erscheint unter „Claude → {ShowRepliesText}“.");
    }

    private void ShowReplies()
    {
        var store = Store();
        var replies = store.Replies(unreadOnly: true);
        if (replies.Count == 0)
        {
            var pending = store.Pending();
            var running = pending.Count(e => e.Status?.State == ClaudeRequestStatus.Running);
            Notify("Information", "Keine neuen Antworten",
                pending.Count == 0 ? "Es gibt keine offenen Anfragen." : $"{pending.Count} Anfrage(n) offen, davon {running} in Arbeit.", null);
            return;
        }

        foreach (var reply in replies.Take(MaxRepliesPerClick))
        {
            var request = store.Get(reply.Id).Request;
            var what = request is null ? reply.Id
                : ClaudeActions.MenuText(request.Action) + ": " + string.Join(", ", request.Selection.Select(i => i.Name ?? i.Kind).Take(5));
            Notify(reply.Status == ClaudeReply.StatusDone ? "Success" : "Warning", "Antwort von Claude", what, reply.Text);
            store.MarkRead(reply.Id, DateTime.UtcNow);
        }

        if (replies.Count > MaxRepliesPerClick)
            Notify("Information", "Weitere Antworten", $"Noch {replies.Count - MaxRepliesPerClick} ungelesene Antwort(en): „{ShowRepliesText}“ erneut wählen.", null);
    }

    private static ClaudeInboxStore Store()
        => new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ClaudeInboxStore.DirectoryName));

    /// <summary>Never lets an exception escape into TIA Portal: it is shown and logged instead.</summary>
    private void Guarded(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            var message = exception is ClaudeInboxException or ClaudeInboxFormatException or IOException or UnauthorizedAccessException
                ? exception.Message
                : exception.GetType().Name + ": " + exception.Message;
            Log("Error", "Claude in TIA: " + message);
            Notify("Error", "Fehler", "Claude in TIA konnte die Aktion nicht ausführen.", message);
        }
    }

    private void Notify(string icon, string message, string detailHeadline, string? detail)
    {
        try
        {
            var box = ((IEngineeringServiceProvider)_portal).GetService<MessageBoxProvider>();
            if (box is not null && RuntimeBinding.Invoke(box, "ShowNotification", icon, Title + " – " + message, detailHeadline, detail ?? string.Empty)) return;
        }
        catch (Exception)
        {
            // Fall back to the inspector window below.
        }

        Log(icon, message + ": " + detailHeadline + (detail is null ? "" : " – " + detail));
    }

    private void Log(string icon, string text)
    {
        try
        {
            var feedback = ((IEngineeringServiceProvider)_portal).GetService<FeedbackProvider>();
            if (feedback is not null) RuntimeBinding.Invoke(feedback, "Log", icon, text);
        }
        catch (Exception)
        {
            // Nothing else can report it.
        }
    }
}

/// <summary>Reads the selection and each object's parent chain (names and type names only, never content).</summary>
internal static class SelectionReader
{
    private const string GlobalLibraryType = "Siemens.Engineering.Library.GlobalLibrary";

    public static IEnumerable<IEngineeringObject> Selected(MenuSelectionProvider<IEngineeringObject> provider)
    {
        var method = provider.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == "GetSelection" && !m.IsGenericMethodDefinition && m.GetParameters().Length == 0)
            ?? throw new MissingMethodException("MenuSelectionProvider.GetSelection() was not found (V21 Add-In API).");
        if (method.Invoke(provider, null) is not IEnumerable result) yield break;
        foreach (var item in result)
            if (item is IEngineeringObject engineeringObject)
                yield return engineeringObject;
    }

    public static (IReadOnlyList<ClaudeChainEntry> Chain, string? ProjectPath, string? GlobalLibraryPath) Chain(IEngineeringObject selected)
    {
        var chain = new List<ClaudeChainEntry>();
        string? projectPath = null, libraryPath = null;
        IEngineeringInstance? current = selected;
        while (current is not null && chain.Count < ClaudeInboxContract.MaxChainEntries)
        {
            var types = TypeNames(current.GetType());
            chain.Add(new ClaudeChainEntry(types.Count == 0 ? new[] { current.GetType().FullName ?? "?" } : types, NameOf(current)));
            if (current is ProjectBase project) projectPath ??= Safe(() => project.Path?.FullName);
            if (types.Contains(GlobalLibraryType)) libraryPath ??= Safe(() => RuntimeBinding.PathProperty(current));
            current = Safe(() => current.Parent);
        }

        return (chain, projectPath, libraryPath);
    }

    public static string? FirstProjectPath(TiaPortal portal) => Safe(() => portal.Projects.FirstOrDefault()?.Path?.FullName);

    private static List<string> TypeNames(Type? type)
    {
        var names = new List<string>();
        for (; type is not null && names.Count < ClaudeInboxContract.MaxTypeNamesPerEntry; type = type.BaseType)
            if (type.FullName is { } name && name.StartsWith("Siemens.Engineering.", StringComparison.Ordinal))
                names.Add(name);
        return names;
    }

    private static string? NameOf(IEngineeringInstance instance)
    {
        if (instance is not IEngineeringObject engineeringObject) return null;
        var name = Safe(() => engineeringObject.GetAttribute("Name") as string);
        return name is { Length: > ClaudeInboxContract.MaxNameChars } ? name.Substring(0, ClaudeInboxContract.MaxNameChars) : name;
    }

    private static T? Safe<T>(Func<T?> read) where T : class
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>Calls members the V21 XML documents only by name and parameter types (no return type): one public instance method of
/// that name whose parameters are an enum followed by strings; the enum member is parsed by name.</summary>
internal static class RuntimeBinding
{
    public static bool Invoke(object target, string name, string enumMember, params string[] texts)
    {
        foreach (var method in target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (method.Name != name) continue;
            var parameters = method.GetParameters();
            if (parameters.Length != texts.Length + 1 || !parameters[0].ParameterType.IsEnum || parameters.Skip(1).Any(p => p.ParameterType != typeof(string))) continue;
            if (!Enum.GetNames(parameters[0].ParameterType).Contains(enumMember, StringComparer.Ordinal)) return false;
            var arguments = new object[texts.Length + 1];
            arguments[0] = Enum.Parse(parameters[0].ParameterType, enumMember);
            Array.Copy(texts, 0, arguments, 1, texts.Length);
            method.Invoke(target, arguments);
            return true;
        }

        return false;
    }

    public static string? PathProperty(object target)
        => target.GetType().GetProperty("Path", BindingFlags.Public | BindingFlags.Instance)?.GetValue(target) switch
        {
            FileSystemInfo info => info.FullName,
            string text => text,
            _ => null,
        };
}
