using System.Reflection;
using System.Runtime.ExceptionServices;

namespace TiaMcpServer.Contracts.PlcOnline;

/// <summary>
/// Audit T1: whether a password configuration may be answered with <c>SetPassword</c>. V21 documents
/// <c>IsSecureCommunication</c> ("Is secure communication used.") on <c>DownloadPasswordConfiguration</c>,
/// <c>OnlinePasswordConfiguration</c>, <c>OnlineAuthenticationConfiguration</c> and <c>UploadPasswordConfiguration</c>.
/// It is read by reflection (exactly one public readable instance <c>bool</c> property) before any password is handed over.
/// <c>true</c>: the password is sent. <c>false</c> or unreadable: the password is NOT sent unless the caller passed
/// <see cref="ArgumentName"/> <c>true</c> for this call; TIA Portal itself allows the non-secure (legacy) channel, so the MCP
/// refuses only by default and never more strictly than TIA Portal (owner principle). The value is recorded as
/// <c>secureCommunication</c> on the configuration entry (null when unreadable). Not live-tested.
/// </summary>
public static class PlcPasswordChannel
{
    /// <summary>
    /// Audit T2: the enum member name V21 documents ("No action") on the download and upload selection configurations
    /// (e.g. <c>StopModulesSelections</c>, <c>OverwriteSystemDataSelections</c>, <c>DifferentTargetConfigurationSelections</c>,
    /// <c>ResetModuleSelections</c>, <c>UploadMissingProductsSelections</c>). An unanswered selection configuration whose live
    /// enum has this exact member is answered with it (source <c>policy</c>) and the abort is recorded WITHOUT throwing from
    /// the callback (K27: a throw from the TLS callback was followed by TIA Portal terminating); TIA Portal then fails or skips
    /// the step itself. Configurations without it (passwords, <c>TargetForSoftware</c>, <c>ConsistentBlocksDownload</c>, ...)
    /// still abort by throwing. Not live-tested.
    /// </summary>
    public const string NoActionSelection = "NoAction";

    public const string PropertyName = "IsSecureCommunication";

    /// <summary>The per-call opt-in of plc_download, plc_tia_online go_online, plc_station_upload and plc_read compare_to_online/compare_hardware_to_online.</summary>
    public const string ArgumentName = "allowInsecurePasswordTransfer";

    /// <summary>The abort reason of a password configuration whose channel is not (known to be) secure and that was not opted in.</summary>
    public const string InsecureCommunicationReason = "insecure_communication";

    /// <summary>
    /// <c>IsSecureCommunication</c> of the live configuration; null when the type has no such single public readable
    /// <c>bool</c> property. The getter's own exception propagates unwrapped, so the caller can classify it.
    /// </summary>
    public static bool? Read(object configuration)
    {
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        var matches = configuration.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => string.Equals(property.Name, PropertyName, StringComparison.Ordinal)
                && property.PropertyType == typeof(bool) && property.GetIndexParameters().Length == 0 && property.GetGetMethod(nonPublic: false) is not null)
            .Take(2)
            .ToArray();
        if (matches.Length != 1) return null;
        try
        {
            return (bool)matches[0].GetValue(configuration, null)!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    /// <summary>Reads the flag; a non-fatal exception counts as unreadable (null), a fatal one propagates.</summary>
    public static bool? TryRead(object configuration, Func<Exception, bool> isFatal)
    {
        try
        {
            return Read(configuration);
        }
        catch (Exception exception) when (!isFatal(exception))
        {
            return null;
        }
    }

    /// <summary>True when the password may be sent: the channel is secure, or the caller opted in for this call.</summary>
    public static bool MaySend(bool? secureCommunication, bool allowInsecure) => secureCommunication == true || allowInsecure;

    /// <summary>The abort detail (never a password or variable name).</summary>
    public static string RefusalDetail(bool? secureCommunication)
        => (secureCommunication is null
                ? $"{PropertyName} could not be read, so it is not known whether the password would travel over a secure connection"
                : $"{PropertyName} is false: TIA Portal would send the password over a non-secure (legacy) connection")
            + $"; the password was not sent. TIA Portal allows this; to send it anyway the user must agree and the call must be retried with {ArgumentName}: true";
}
