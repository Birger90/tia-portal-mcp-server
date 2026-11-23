using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.PlcDownload;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.ProjectLifecycle;

/// <summary>
/// The UMAC login of <c>open_project</c> (idea: Sparkz400/TIA_MCP 315493b, Apache-2.0; reimplemented with the fork's password
/// sources). The password source is redacted while binding exactly like the plc_download passwords, so no caller value that might
/// be a password reaches the pipeline, a response or the audit; an omitted source comes from the password map (purpose
/// <see cref="UmacProjectLoginNames.PasswordPurpose"/>, project = the project file name).
/// </summary>
public static class UmacOpenItem
{
    public static LifecycleWriteItem Build(LifecycleWriteItem item, string? userName, string? userType, string? password,
        Func<string, string?> readEnvironment)
    {
        if (userName is null && userType is null && password is null) return item;
        var token = password is null ? null : PlcDownloadPasswordMap.Redact(password, readEnvironment);
        var fromMap = false;
        if (token is null && userName is not null
            && PasswordMapFile.Lookup(PasswordMapStore.Entries, PasswordMapStore.ProjectName(item.ProjectPath), null, UmacProjectLoginNames.PasswordPurpose) is { } mapped)
        {
            token = PasswordSources.KindOf(mapped) == PasswordSourceKind.Environment ? PlcDownloadPasswordMap.Redact(mapped, readEnvironment) : mapped;
            fromMap = true;
        }

        return item with { UmacUserName = userName, UmacUserType = userType, UmacPassword = token, UmacPasswordFromMap = fromMap };
    }

    /// <summary>Why the UMAC fields of an open_project item are not usable; null when they are (or absent).</summary>
    public static string? Error(LifecycleWriteItem item)
    {
        if (item.UmacUserName is null)
            return item.UmacUserType is null && item.UmacPassword is null ? null : "umacUserType and umacPassword need umacUserName.";
        if (UmacProjectLoginNames.UserNameError(item.UmacUserName) is { } nameError) return nameError;
        if (item.UmacUserType is not null && UmacProjectLoginNames.UserTypeError(item.UmacUserType) is { } typeError) return typeError;
        if (!string.Equals(Path.GetExtension(item.ProjectPath), ".ap21", StringComparison.OrdinalIgnoreCase))
            return "A UMAC login applies to an .ap21 project file (ProjectComposition.Open(FileInfo, UmacDelegate)); a .als21 local session is opened without it.";
        if (item.Upgrade)
            return "A UMAC login is not offered together with upgrade (no two-argument OpenWithUpgrade(FileInfo, UmacDelegate) is documented); open without upgrade.";
        if (item.UmacPassword is null)
            return $"umacPassword is required: name an environment variable or credential:<target>, or add a password map entry of purpose {UmacProjectLoginNames.PasswordPurpose} for this project.";
        if (item.UmacPassword == PlcDownloadPasswordMap.UnsetVariable)
            return "umacPassword names an environment variable that is not set or is empty in the server process.";
        if (!PasswordSources.IsSource(item.UmacPassword))
            return "umacPassword must be an environment variable NAME or credential:<target> with the owner's credential prefix, never the password itself.";
        return null;
    }

    /// <summary>The confirmation sentence: user and type quoted, the source only by kind.</summary>
    public static string PromptSentence(LifecycleWriteItem item)
        => $" as UMAC user {ConfirmationText.Quote(item.UmacUserName)} ({item.UmacUserType ?? UmacProjectLoginNames.ProjectUser} user; password from "
            + $"{(item.UmacPasswordFromMap ? "the password map, " : string.Empty)}a {PasswordSources.KindName(item.UmacPassword) ?? "password"} source, sent once)";
}
