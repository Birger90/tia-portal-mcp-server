namespace TiaMcpServer.Contracts.Plc.DeviceData;

/// <summary>
/// Further download variants and the PLC master secret (GitHub issue #16). Evidence: the V21 IntelliSense file
/// Siemens.Engineering.Base.xml: <c>DownloadProvider.Download(DirectoryInfo, DownloadConfigurationDelegate)</c> and
/// <c>RHDownloadProvider.Download(DirectoryInfo, DownloadConfigurationDelegate)</c> ("Downloads hardware and software into a folder"),
/// <c>OnlineProvider.SetPlcMasterSecret(SecureString)</c> and <c>OnlineProvider.ResetPlcMasterSecret()</c> ("Delete the Plc Master
/// Secret"). The worker methods carry the public operation names. Not live-tested.
/// </summary>
public static class PlcDeviceDataMethods
{
    /// <summary>plc_write: the configuration of one PLC (or R/H system) downloaded into a Windows folder; no device is touched (ProjectMutation: caller files).</summary>
    public const string DownloadToFolder = "download_to_folder";

    /// <summary>plc_write: sets the master secret of the online PLC (OnlineControl; the PLC must be online through plc_tia_online).</summary>
    public const string SetMasterSecretOnline = "set_plc_master_secret_online";

    /// <summary>plc_write: deletes the master secret of the online PLC (OnlineControl).</summary>
    public const string ResetMasterSecretOnline = "reset_plc_master_secret_online";

    public static IReadOnlyList<string> Write { get; } = new[] { DownloadToFolder, SetMasterSecretOnline, ResetMasterSecretOnline };

    public static IReadOnlyList<string> Online { get; } = new[] { SetMasterSecretOnline, ResetMasterSecretOnline };

    public static bool IsWorkerMethod(string? method) => method is not null && Write.Contains(method, StringComparer.Ordinal);

    public static bool IsOnline(string? method) => method is not null && Online.Contains(method, StringComparer.Ordinal);
}

public static class PlcDeviceDataNames
{
    public const string RhDownloadProvider = "Siemens.Engineering.Download.RHDownloadProvider";
    public const string DownloadConfigurationDelegate = "Siemens.Engineering.Download.DownloadConfigurationDelegate";
    public const int MaxFiles = 1000;
}

public sealed class PlcDeviceDataRequestInfo
{
    public string Operation { get; set; } = string.Empty;

    public string? PlcName { get; set; }

    /// <summary>download_to_folder: the directory the configuration is downloaded into (must exist).</summary>
    public string? DirectoryPath { get; set; }

    /// <summary>download_to_folder: use the R/H system's RHDownloadProvider instead of the PLC's DownloadProvider.</summary>
    public bool RhSystem { get; set; }

    /// <summary>set_plc_master_secret_online: the password source (environment variable NAME or credential target), never the secret.</summary>
    public string? PasswordVariable { get; set; }

    /// <summary>
    /// The item's portalConfirmations policy (decline or accept) for the TIA Portal dialogs raised while it runs: download_to_folder
    /// and, since the security review 2026-11-03 (F6), the online master secret operations.
    /// </summary>
    public string PortalConfirmations { get; set; } = PlcDownloadPolicy.PortalConfirmationPolicies.Decline;

    /// <summary>download_to_folder: a non-empty target directory is refused unless true (security review F3).</summary>
    public bool Overwrite { get; set; }
}

/// <summary>The Siemens-free rules of download_to_folder's target directory and of the confirmation evidence (security review F3, F6).</summary>
public static class PlcDeviceDataPolicy
{
    /// <summary>
    /// Why <paramref name="directory"/> cannot take the download (it does not exist, it holds files or folders and
    /// <paramref name="overwrite"/> is false, or the link check refuses it), with the location warning to report; both null
    /// when it can. The link and junction check is <see cref="PlcFileTargets.InspectDirectory"/>, as for the other directory exports.
    /// </summary>
    public static (string? Refusal, string? Warning) TargetError(string directory, bool overwrite)
    {
        if (!Directory.Exists(directory)) return ($"directoryPath '{directory}' does not exist. Nothing was written.", null);
        var location = PlcFileTargets.InspectDirectory(directory);
        if (location.Refusal is { } refusal) return (refusal, null);
        if (!overwrite && Directory.EnumerateFileSystemEntries(directory).Any())
            return (NotEmpty(directory), location.Warning);
        return (null, location.Warning);
    }

    /// <summary>The refusal of a non-empty target directory without overwrite:true.</summary>
    public static string NotEmpty(string directory)
        => $"directoryPath '{directory}' is not empty; TIA Portal may replace the files there. Pass overwrite:true to download into it anyway, or choose an empty directory. Nothing was written.";

    /// <summary>
    /// The failure message when the item's scope answered TIA Portal dialogs No (any dialog under decline: every one is answered
    /// No, recorded or not), or null when none was declined.
    /// </summary>
    public static string? Declined(string operation, IReadOnlyList<PlcDownloadPortalConfirmationInfo> entries, int observed)
    {
        var declined = entries.Where(e => e.Answer == PlcDownloadPolicy.PortalConfirmationAnswers.No).ToList();
        var unrecorded = Math.Max(0, observed - entries.Count);
        if (declined.Count == 0 && (unrecorded == 0 || entries.Any(e => e.Answer == PlcDownloadPolicy.PortalConfirmationAnswers.Yes))) return null;
        var listed = string.Join("; ", declined.Take(3).Select(e => e.Text is null ? e.Type : $"{e.Type}: {e.Text}"));
        return $"{operation}: TIA Portal raised {observed} confirmation dialog(s), answered No under portalConfirmations decline ({(listed.Length == 0 ? "not recorded" : listed)}). "
            + "The item failed; it may have stopped part way. Check the dialog and pass portalConfirmations: accept to answer Yes.";
    }

    /// <summary>The warning for dialogs the item's scope answered Yes (accept), or null when none was.</summary>
    public static string? Accepted(string operation, IReadOnlyList<PlcDownloadPortalConfirmationInfo> entries)
    {
        var accepted = entries.Where(e => e.Answer == PlcDownloadPolicy.PortalConfirmationAnswers.Yes).ToList();
        return accepted.Count == 0 ? null
            : $"{operation}: {accepted.Count} TIA Portal confirmation dialog(s) were answered Yes under portalConfirmations accept ({string.Join("; ", accepted.Take(3).Select(e => e.Text is null ? e.Type : $"{e.Type}: {e.Text}"))}).";
    }
}

public sealed class PlcDeviceDataFileInfo
{
    public string Path { get; set; } = string.Empty;

    public long Bytes { get; set; }

    public string Sha256 { get; set; } = string.Empty;
}

public sealed class PlcDeviceDataResultInfo
{
    public string Operation { get; set; } = string.Empty;

    public string PlcName { get; set; } = string.Empty;

    public string? DeviceName { get; set; }

    /// <summary>The Siemens member called.</summary>
    public string Call { get; set; } = string.Empty;

    /// <summary>download_to_folder: the directory and the files written by the call, re-hashed.</summary>
    public string? DirectoryPath { get; set; }

    public List<PlcDeviceDataFileInfo> Files { get; set; } = new();

    /// <summary>download_to_folder: the download configurations TIA Portal presented and how each was answered (type short name: answer).</summary>
    public List<string> ConfigurationsSeen { get; set; } = new();

    /// <summary>The online state read before an online call (Online is required).</summary>
    public string? OnlineState { get; set; }

    /// <summary>The password source used (its name, never the secret); null when none.</summary>
    public string? PasswordSource { get; set; }

    /// <summary>The TIA Portal dialogs answered while the item ran, by its portalConfirmations policy (security review F6).</summary>
    public List<PlcDownloadPortalConfirmationInfo> PortalConfirmations { get; set; } = new();

    /// <summary>How many dialogs were observed (recorded or not).</summary>
    public int PortalConfirmationsObserved { get; set; }

    /// <summary>download_to_folder: true when overwrite:true let the download go into a non-empty directory.</summary>
    public bool Overwrite { get; set; }

    /// <summary>verified (files written and hashed), notReadBack (an online call: the master secret cannot be read back).</summary>
    public string Evidence { get; set; } = string.Empty;

    public string? Note { get; set; }
}
