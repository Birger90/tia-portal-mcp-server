using System.Reflection;

namespace TiaMcpServer.Contracts.Worker;

/// <summary>
/// One TIA Portal <c>Notification</c> (an information message such as "Export erfolgreich abgeschlossen. (0032:000002)") that the
/// worker handled. Evidence: V21 <c>E:Siemens.Engineering.TiaPortal.Notification</c> and <c>NotificationEventArgs</c> with
/// <c>Caption</c>, <c>Text</c>, <c>DetailText</c>, <c>Icon</c> (<c>NotificationIcon</c> Success/Information/Warning/Error) and
/// <c>IsHandled</c> (Siemens.Engineering.Base.xml). The members are read by reflection; null where absent or unreadable.
/// </summary>
public sealed class PortalNotificationInfo
{
    public string? Caption { get; set; }

    public string? Text { get; set; }

    public string? DetailText { get; set; }

    /// <summary>The <c>NotificationIcon</c> member name.</summary>
    public string? Icon { get; set; }

    /// <summary>True when <c>IsHandled</c> was set to true (TIA Portal then does not wait for a human to dismiss the message).</summary>
    public bool Handled { get; set; }
}

/// <summary>
/// The Siemens-free handling of a TIA Portal <c>Notification</c> (K75, live 2026-11-03: <c>export_project_texts</c> blocked until a
/// human clicked OK on a modal information message). <see cref="Handle"/> reads Caption/Text/DetailText/Icon and sets
/// <c>IsHandled = true</c>, all by reflection on the runtime event arguments (the reference stubs declare only <c>Text</c>; no stub
/// member was added). Shared by the worker session and the tests' doubles. Not live-tested.
/// </summary>
public static class PortalNotificationHandling
{
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

    /// <summary>At most this many notifications are kept per request (and between requests); the rest are only counted.</summary>
    public const int MaxRecorded = 20;

    public static PortalNotificationInfo Handle(object eventArgs)
    {
        if (eventArgs is null) throw new ArgumentNullException(nameof(eventArgs));
        var info = new PortalNotificationInfo
        {
            Caption = ReadText(eventArgs, "Caption"),
            Text = ReadText(eventArgs, "Text"),
            DetailText = ReadText(eventArgs, "DetailText"),
            Icon = ReadText(eventArgs, "Icon"),
        };
        try
        {
            var property = eventArgs.GetType().GetProperty("IsHandled", PublicInstance);
            if (property is { CanWrite: true } && property.PropertyType == typeof(bool) && property.GetIndexParameters().Length == 0)
            {
                property.SetValue(eventArgs, true, null);
                info.Handled = !property.CanRead || property.GetValue(eventArgs, null) is true;
            }
        }
        catch (Exception)
        {
            info.Handled = false;
        }

        return info;
    }

    /// <summary>One warning line: caption, text, detail and icon, flattened and shortened.</summary>
    public static string Describe(PortalNotificationInfo info)
    {
        var parts = new List<string>();
        if (Flat(info.Caption) is { } caption) parts.Add($"\"{caption}\"");
        if (Flat(info.Text) is { } text) parts.Add(text);
        if (Flat(info.DetailText) is { } detail) parts.Add($"({detail})");
        if (!string.IsNullOrWhiteSpace(info.Icon)) parts.Add($"[{info.Icon}]");
        var body = parts.Count == 0 ? "(no readable text)" : string.Join(" ", parts);
        if (body.Length > 400) body = body.Substring(0, 400) + "...";
        return body;
    }

    /// <summary>The warning a notification handled while a request runs adds to that request's response.</summary>
    public static string RequestWarning(PortalNotificationInfo info)
        => $"TIA Portal notification during this call: {Describe(info)}; "
            + (info.Handled ? "handled by the MCP (IsHandled = true), so TIA Portal did not wait for a click on OK."
                : "IsHandled could not be set; TIA Portal may show the message until someone clicks OK.");

    private static string? ReadText(object eventArgs, string name)
    {
        try
        {
            var property = eventArgs.GetType().GetProperty(name, PublicInstance);
            if (property is not { CanRead: true } || property.GetIndexParameters().Length != 0) return null;
            return property.GetValue(eventArgs, null)?.ToString();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? Flat(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : string.Join(" ", value!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
