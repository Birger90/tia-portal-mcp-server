namespace TiaMcpServer.Contracts.Shared.PasswordManagement;

/// <summary>What a check of a password source found. A check never reads, shows or compares the password itself.</summary>
public enum CredentialState
{
    /// <summary>The generic credential exists and holds a non-empty UTF-16 password.</summary>
    Present,

    /// <summary>No generic credential with the target exists for this Windows user.</summary>
    Missing,

    /// <summary>The credential exists but its password is empty or not UTF-16 (the server cannot use it).</summary>
    Unusable,

    /// <summary>The target does not start with the credential prefix the server accepts.</summary>
    PrefixMismatch,

    /// <summary>The environment variable is set in this program's environment.</summary>
    EnvironmentSet,

    /// <summary>The environment variable is not set in this program's environment.</summary>
    EnvironmentUnset,

    /// <summary>Windows Credential Manager is not available (another operating system).</summary>
    Unsupported,

    /// <summary>The check failed (Win32 error).</summary>
    Failed,
}

/// <summary>The outcome of a check, with a German message that never contains a password.</summary>
public sealed class CredentialCheckResult
{
    public CredentialCheckResult(CredentialState state, string message)
    {
        State = state;
        Message = message;
    }

    public CredentialState State { get; }

    public string Message { get; }

    /// <summary>True when the server can use the source as far as this program can tell.</summary>
    public bool Usable => State is CredentialState.Present or CredentialState.EnvironmentSet;
}

/// <summary>
/// Writes, deletes and checks Windows generic credentials for the password manager. Implementations never return, log or
/// keep a password; <see cref="Write"/> takes the secret as a character array the caller clears afterwards.
/// </summary>
public interface ICredentialStore
{
    /// <summary>Whether a usable credential with <paramref name="target"/> exists (its value is never read out).</summary>
    CredentialCheckResult Check(string target);

    /// <summary>Creates or replaces the generic credential; returns the refusal (German) or null.</summary>
    string? Write(string target, char[] secret, string comment);

    /// <summary>Deletes the generic credential (an absent one is no error); returns the refusal (German) or null.</summary>
    string? Delete(string target);
}
