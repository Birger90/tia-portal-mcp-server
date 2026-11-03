using TiaMcpServer.PlcWebApi;

namespace TiaMcpServer.Diagnostics.Checks;

/// <summary>
/// Parses the PLC Web API target file the server would load (<c>--plc-webapi-config</c> or
/// <c>TIA_MCP_PLC_WEBAPI_CONFIG</c>) and reports its targets. It never contacts a PLC and never
/// reads a credential value: for each target it reports only whether the named environment
/// variables are set. No file configured passes (the online tools are optional); an invalid file
/// fails because the server refuses to start with it.
/// </summary>
public sealed class PlcWebApiConfigCheck : IDiagnosticCheck
{
    private readonly IEnvironmentVariableService _env;
    private readonly PlcWebApiConfigOptionResult _option;
    private readonly Func<string, PlcWebApiConfigLoadResult> _load;

    public PlcWebApiConfigCheck(IEnvironmentVariableService env, PlcWebApiConfigOptionResult? option,
        Func<string, PlcWebApiConfigLoadResult>? load = null)
    {
        _env = env;
        _option = option ?? PlcWebApiConfigOption.Parse(Array.Empty<string>(), env.Get);
        _load = load ?? (path => PlcWebApiConfigLoader.Load(path));
    }

    public string Id => "plc-webapi-config";

    public string Name => "PLC Web API targets (optional)";

    public DiagnosticCheckResult Run()
    {
        var evidence = Evidence.Empty();
        evidence["option"] = PlcWebApiConfigOption.Flag;
        evidence["environmentVariable"] = PlcWebApiConfigOption.EnvVar;
        evidence["source"] = PlcWebApiConfigOptionResult.SourceName(_option.Source);
        evidence["path"] = _option.Path;
        if (!_option.IsValid)
            return new DiagnosticCheckResult(Id, Name, DiagnosticStatus.Failed, _option.Error ?? "The PLC Web API configuration option is invalid.",
                $"Give {PlcWebApiConfigOption.Flag} (or {PlcWebApiConfigOption.EnvVar}) the absolute path of the target file.", evidence);
        if (_option.Path is null)
            return new DiagnosticCheckResult(Id, Name, DiagnosticStatus.Passed,
                "No PLC Web API target file is configured; plc_online_read and plc_online_write report target_not_found until one is.",
                null, evidence);

        var loaded = _load(_option.Path);
        if (!loaded.IsValid)
        {
            for (var index = 0; index < loaded.Errors.Count; index++) evidence[$"error[{index}]"] = loaded.Errors[index];
            return new DiagnosticCheckResult(Id, Name, DiagnosticStatus.Failed,
                $"The PLC Web API target file '{_option.Path}' is invalid ({loaded.Errors.Count} problem(s)); the MCP server refuses to start with it.",
                "Fix the file; see docs/SupportedOperations/PLC_ONLINE_WEBAPI_SUMMARY.md for the format.", evidence);
        }

        var targets = loaded.Configuration!.Targets;
        var warnings = new List<string>();
        evidence["targetCount"] = targets.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        for (var index = 0; index < targets.Count; index++)
        {
            var target = targets[index];
            var prefix = $"target[{index}]";
            evidence[$"{prefix}.name"] = target.Name;
            evidence[$"{prefix}.address"] = target.Address;
            evidence[$"{prefix}.certificateValidation"] = target.CertificateValidationName;
            evidence[$"{prefix}.requestTimeoutSeconds"] = ((int)target.RequestTimeout.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
            evidence[$"{prefix}.authentication"] = target.IsAnonymous ? "anonymous" : "user";
            if (!target.IsAnonymous)
            {
                var userSet = !string.IsNullOrEmpty(_env.Get(target.UserEnv!));
                var passwordSet = _env.Get(target.PasswordEnv!) is not null;
                evidence[$"{prefix}.userEnv"] = target.UserEnv;
                evidence[$"{prefix}.userEnvSet"] = userSet.ToString().ToLowerInvariant();
                evidence[$"{prefix}.passwordEnv"] = target.PasswordEnv;
                evidence[$"{prefix}.passwordEnvSet"] = passwordSet.ToString().ToLowerInvariant();
                if (!userSet || !passwordSet)
                    warnings.Add($"target '{target.Name}': {(userSet ? target.PasswordEnv : target.UserEnv)} is not set in this environment");
            }

            if (target.CertificateValidation == PlcWebApiCertificateValidation.None)
                warnings.Add($"target '{target.Name}': certificate validation is 'none' (the PLC's TLS certificate is not checked)");
        }

        var summary = $"{targets.Count} PLC Web API target(s) in '{_option.Path}': "
            + (targets.Count == 0 ? "none" : string.Join(", ", targets.Select(target => $"{target.Name} ({target.Address}, {target.CertificateValidationName})")))
            + ". doctor makes no network calls; run plc_online_read ping to reach a target.";
        return warnings.Count == 0
            ? new DiagnosticCheckResult(Id, Name, DiagnosticStatus.Passed, summary, null, evidence)
            : new DiagnosticCheckResult(Id, Name, DiagnosticStatus.Warning, summary + " Warnings: " + string.Join("; ", warnings) + ".",
                "Set the credential environment variables in the MCP client's server configuration (the same environment the server runs in) "
                + "and prefer certificate validation 'system' or 'thumbprint'.", evidence);
    }
}
