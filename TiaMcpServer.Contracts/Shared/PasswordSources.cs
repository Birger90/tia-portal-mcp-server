using System.Runtime.InteropServices;
using System.Security;
using TiaMcpServer.Contracts.Plc;

namespace TiaMcpServer.Contracts.Shared;

/// <summary>Where a password comes from. The value itself never crosses the protocol, a response, a log or the audit.</summary>
public enum PasswordSourceKind
{
    /// <summary>Not a valid source token (a value redacted at binding, or malformed).</summary>
    Invalid,

    /// <summary>An environment variable of the server process (the worker inherits it), named by the token itself.</summary>
    Environment,

    /// <summary>A Windows Credential Manager generic credential, named by its target after <see cref="PasswordSources.CredentialTokenPrefix"/>.</summary>
    Credential,
}

/// <summary>Reads one generic credential's secret into a read-only <see cref="SecureString"/>; null with a reason when it cannot.</summary>
public interface ICredentialReader
{
    SecureString? Read(string target, out string? reason);
}

/// <summary>
/// The one password-source rule shared by every password input (plc_download <c>passwords</c>, plc_tia_online and
/// plc_station_upload <c>passwords</c>, the fail-safe <c>passwordVariable</c>, the password map file). A source travels
/// as one token string: an environment variable NAME as before (backward compatible), or <c>credential:</c> followed by a
/// Windows Credential Manager generic-credential target name. Target names must start with the active prefix
/// (<see cref="ActiveCredentialPrefix"/>, default <see cref="CredentialTargetPrefix"/>), so a caller can never make the
/// server read an unrelated credential of the user (a browser, Git or VPN secret) and hand it to TIA Portal. Only the
/// owner sets another prefix, through the environment variable <see cref="CredentialPrefixVariable"/> in the MCP client
/// configuration (read once at process start by the host and by the worker, which inherits it; never settable by a tool
/// call); <see cref="AnyCredentialPrefix"/> allows every generic credential target.
/// </summary>
public static class PasswordSources
{
    public const string CredentialTokenPrefix = "credential:";

    /// <summary>The default credential target prefix (ordinal, case-sensitive).</summary>
    public const string CredentialTargetPrefix = "TiaMcp/";

    /// <summary>The owner's server configuration of the prefix (MCP client <c>env</c>); never a tool argument.</summary>
    public const string CredentialPrefixVariable = "TIA_MCP_CREDENTIAL_PREFIX";

    /// <summary>The configuration value that allows every generic credential target.</summary>
    public const string AnyCredentialPrefix = "*";

    /// <summary>The longest configurable prefix (it must leave room for a name within CRED_MAX_STRING_LENGTH).</summary>
    public const int MaxCredentialPrefixLength = 128;

    /// <summary>The prefix every credential target must start with; <see cref="AnyCredentialPrefix"/> means none is required.</summary>
    public static string ActiveCredentialPrefix { get; private set; } = CredentialTargetPrefix;

    /// <summary>Where <see cref="ActiveCredentialPrefix"/> came from: <c>default</c> or <c>environment</c>.</summary>
    public static string ActiveCredentialPrefixSource { get; private set; } = "default";

    /// <summary>True when every generic credential target is accepted (owner configuration <c>*</c>).</summary>
    public static bool AcceptsAnyCredentialTarget => ActiveCredentialPrefix == AnyCredentialPrefix;

    /// <summary>Why a configured prefix is not accepted; null when it is (an unset or empty value means the default).</summary>
    public static string? CredentialPrefixError(string? value)
    {
        if (string.IsNullOrEmpty(value) || value == AnyCredentialPrefix) return null;
        if (value!.Length > MaxCredentialPrefixLength) return $"{CredentialPrefixVariable} has at most {MaxCredentialPrefixLength} characters";
        if (value.Trim() != value) return $"{CredentialPrefixVariable} must not start or end with blanks";
        if (value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) && c != ' ' || c == '*'))
            return $"{CredentialPrefixVariable} must not contain control characters, tabs, line breaks or '*' (use exactly '*' to allow every target)";
        return null;
    }

    /// <summary>
    /// Applies the owner's configuration once at process start (<paramref name="readEnvironment"/> reads
    /// <see cref="CredentialPrefixVariable"/>); an unset or empty value keeps the default. Returns the refusal for an
    /// invalid value (the process then refuses to start), or null.
    /// </summary>
    public static string? ConfigureCredentialPrefix(Func<string, string?> readEnvironment)
    {
        var value = readEnvironment(CredentialPrefixVariable);
        if (CredentialPrefixError(value) is { } error) return error + ".";
        ActiveCredentialPrefix = string.IsNullOrEmpty(value) ? CredentialTargetPrefix : value!;
        ActiveCredentialPrefixSource = string.IsNullOrEmpty(value) ? "default" : "environment";
        return null;
    }

    /// <summary>The active prefix as status output shows it (<c>* (any generic credential)</c> for the wildcard).</summary>
    public static string DescribeCredentialPrefix()
        => (AcceptsAnyCredentialTarget ? "* (any generic credential target)" : ActiveCredentialPrefix) + $" ({ActiveCredentialPrefixSource})";

    /// <summary>CRED_MAX_STRING_LENGTH of wincred.h.</summary>
    public const int MaxCredentialTargetLength = 256;

    public const string EnvironmentKindName = "environment";
    public const string CredentialKindName = "credential";

    public static PasswordSourceKind KindOf(string? token)
    {
        if (token is null) return PasswordSourceKind.Invalid;
        if (token.StartsWith(CredentialTokenPrefix, StringComparison.Ordinal))
            return CredentialTargetError(token.Substring(CredentialTokenPrefix.Length)) is null ? PasswordSourceKind.Credential : PasswordSourceKind.Invalid;
        return PlcDownloadPolicy.IsEnvironmentVariableName(token) ? PasswordSourceKind.Environment : PasswordSourceKind.Invalid;
    }

    public static bool IsSource(string? token) => KindOf(token) != PasswordSourceKind.Invalid;

    /// <summary>The token of a credential target (call <see cref="CredentialTargetError"/> first).</summary>
    public static string CredentialToken(string target) => CredentialTokenPrefix + target;

    /// <summary>The target of a credential token, or null.</summary>
    public static string? CredentialTarget(string? token)
        => KindOf(token) == PasswordSourceKind.Credential ? token!.Substring(CredentialTokenPrefix.Length) : null;

    /// <summary>The target after <see cref="CredentialTokenPrefix"/> without checking it, or null when the token is no credential token.</summary>
    public static string? CredentialTargetOf(string? token)
        => token is not null && token.StartsWith(CredentialTokenPrefix, StringComparison.Ordinal) ? token.Substring(CredentialTokenPrefix.Length) : null;

    /// <summary>Why a credential target name is not accepted; null when it is. The message never repeats the name.</summary>
    public static string? CredentialTargetError(string? target) => CredentialTargetError(target, ActiveCredentialPrefix);

    /// <summary>The rule of <see cref="CredentialTargetError(string?)"/> for a given prefix (<see cref="AnyCredentialPrefix"/>: any target).</summary>
    public static string? CredentialTargetError(string? target, string prefix)
    {
        if (string.IsNullOrEmpty(target)) return "a credential target name is required";
        if (target!.Length > MaxCredentialTargetLength) return $"a credential target name has at most {MaxCredentialTargetLength} characters";
        if (prefix != AnyCredentialPrefix && (!target.StartsWith(prefix, StringComparison.Ordinal) || target.Length == prefix.Length))
            return $"a credential target name must start with '{prefix}' followed by a name (for example {prefix}Project/PLC_1/Safety; the prefix is the server owner's setting {CredentialPrefixVariable})";
        if (target.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) && c != ' ' || c == '*'))
            return "a credential target name must not contain control characters, tabs, line breaks or '*'";
        return null;
    }

    /// <summary>The kind name recorded in results and the audit (<c>environment</c> or <c>credential</c>), or null for an invalid token.</summary>
    public static string? KindName(string? token) => KindOf(token) switch
    {
        PasswordSourceKind.Environment => EnvironmentKindName,
        PasswordSourceKind.Credential => CredentialKindName,
        _ => null,
    };

    /// <summary>
    /// The password of <paramref name="token"/> as a read-only <see cref="SecureString"/> (the caller disposes it); null with
    /// <paramref name="reason"/> when the source is invalid, unset or unreadable. Reasons never contain the secret.
    /// </summary>
    public static SecureString? Resolve(string? token, Func<string, string?> readEnvironment, ICredentialReader? credentials, out string? reason)
    {
        switch (KindOf(token))
        {
            case PasswordSourceKind.Environment:
            {
                var value = readEnvironment(token!);
                if (string.IsNullOrEmpty(value))
                {
                    reason = "The environment variable named for this password is not set or is empty in the worker process.";
                    return null;
                }

                var secure = new SecureString();
                foreach (var character in value!) secure.AppendChar(character);
                secure.MakeReadOnly();
                reason = null;
                return secure;
            }
            case PasswordSourceKind.Credential:
                return (credentials ?? WindowsCredentialReader.Instance).Read(token!.Substring(CredentialTokenPrefix.Length), out reason);
            default:
                reason = AcceptsAnyCredentialTarget
                    ? "The password entry names neither a valid environment variable nor a valid credential target."
                    : "The password entry names neither a valid environment variable nor a credential target starting with '" + ActiveCredentialPrefix + "'.";
                return null;
        }
    }
}

/// <summary>
/// Reads Windows Credential Manager generic credentials with the documented Win32 <c>CredReadW</c> (advapi32,
/// <c>CRED_TYPE_GENERIC</c>) and <c>CredFree</c>. The blob is the UTF-16LE password that <c>cmdkey /generic:... /pass</c>
/// and the Credential Manager UI store; it is copied character by character into a <see cref="SecureString"/> (no managed
/// string), then zeroed before <c>CredFree</c>. On other operating systems every read fails with a clear reason.
/// </summary>
public sealed class WindowsCredentialReader : ICredentialReader
{
    public static WindowsCredentialReader Instance { get; } = new();

    public const string UnsupportedReason = "Windows Credential Manager password sources are supported only on Windows; use an environment variable on this system.";

    private const int CredTypeGeneric = 1;
    private const int ErrorNotFound = 1168;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr buffer);

    public SecureString? Read(string target, out string? reason)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            reason = UnsupportedReason;
            return null;
        }

        if (PasswordSources.CredentialTargetError(target) is { } invalid)
        {
            reason = "The credential target is not accepted: " + invalid + ".";
            return null;
        }

        if (!CredRead(target, CredTypeGeneric, 0, out var handle))
        {
            var error = Marshal.GetLastWin32Error();
            reason = error == ErrorNotFound
                ? "No Windows generic credential with the named target exists for the user the server runs as; store it once with cmdkey /generic."
                : $"CredReadW failed with Win32 error {error}.";
            return null;
        }

        try
        {
            var credential = (Credential)Marshal.PtrToStructure(handle, typeof(Credential))!;
            var size = credential.CredentialBlobSize;
            var blob = credential.CredentialBlob;
            try
            {
                if (size <= 0 || blob == IntPtr.Zero)
                {
                    reason = "The Windows generic credential with the named target has an empty password.";
                    return null;
                }

                if (size % 2 != 0)
                {
                    reason = "The Windows generic credential with the named target does not hold a UTF-16 password (store it with cmdkey /generic ... /pass).";
                    return null;
                }

                var secure = new SecureString();
                for (var offset = 0; offset < size; offset += 2) secure.AppendChar((char)Marshal.ReadInt16(blob, offset));
                secure.MakeReadOnly();
                reason = null;
                return secure;
            }
            finally
            {
                if (blob != IntPtr.Zero)
                    for (var offset = 0; offset < size; offset++) Marshal.WriteByte(blob, offset, 0);
            }
        }
        finally
        {
            CredFree(handle);
        }
    }
}
