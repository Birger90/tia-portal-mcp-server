using TiaMcpServer.Contracts.Shared;

namespace TiaMcpServer.Diagnostics.Checks;

/// <summary>
/// Reports the owner's password-source configuration the server would start with: the credential target prefix
/// (<see cref="PasswordSources.CredentialPrefixVariable"/>, default <see cref="PasswordSources.CredentialTargetPrefix"/>,
/// <c>*</c> for every generic credential) and the names-only password map path. It never reads a credential or a
/// password value. An invalid prefix fails because the server refuses to start with it.
/// </summary>
public sealed class PasswordSourcesCheck(IEnvironmentVariableService env) : IDiagnosticCheck
{
    public string Id => "password-sources";

    public string Name => "Password sources (credential prefix, password map)";

    public DiagnosticCheckResult Run()
    {
        var evidence = Evidence.Empty();
        var configured = env.Get(PasswordSources.CredentialPrefixVariable);
        evidence["environmentVariable"] = PasswordSources.CredentialPrefixVariable;
        evidence["passwordMapVariable"] = "TIA_MCP_PASSWORD_MAP";
        evidence["passwordMap"] = env.Get("TIA_MCP_PASSWORD_MAP") ?? "%APPDATA%\\TiaMcpServer\\passwords.json (when present)";
        if (PasswordSources.CredentialPrefixError(configured) is { } error)
        {
            evidence["credentialPrefix"] = null;
            return new DiagnosticCheckResult(Id, Name, DiagnosticStatus.Failed, error + "; the MCP server refuses to start with it.",
                $"Set {PasswordSources.CredentialPrefixVariable} in the MCP client configuration to a prefix such as {PasswordSources.CredentialTargetPrefix}, to {PasswordSources.AnyCredentialPrefix}, or remove it.", evidence);
        }

        var active = string.IsNullOrEmpty(configured) ? PasswordSources.CredentialTargetPrefix : configured!;
        evidence["credentialPrefix"] = active;
        evidence["source"] = string.IsNullOrEmpty(configured) ? "default" : "environment";
        var message = active == PasswordSources.AnyCredentialPrefix
            ? "Credential prefix '*': every Windows generic credential target may be named as a password source (owner setting)."
            : $"Credential targets must start with '{active}'.";
        return new DiagnosticCheckResult(Id, Name, DiagnosticStatus.Passed, message, null, evidence);
    }
}
