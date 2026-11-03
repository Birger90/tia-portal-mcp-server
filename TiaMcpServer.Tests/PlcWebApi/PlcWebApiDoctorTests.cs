using TiaMcpServer.Cli;
using TiaMcpServer.Diagnostics;
using TiaMcpServer.Diagnostics.Checks;
using TiaMcpServer.PlcWebApi;
using TiaMcpServer.Tests.Cli;
using TiaMcpServer.Tests.Diagnostics;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.PlcWebApi;

public sealed class PlcWebApiDoctorTests : IDisposable
{
    private readonly TempAuditDirectory _directory = new();

    public PlcWebApiDoctorTests() => Directory.CreateDirectory(_directory.Path);

    public void Dispose() => _directory.Dispose();

    private string File(string json)
    {
        var path = Path.Combine(_directory.Path, Guid.NewGuid().ToString("N") + ".json");
        System.IO.File.WriteAllText(path, json);
        return path;
    }

    private static DiagnosticCheckResult Run(FakeEnvironmentVariableService env, params string[] args)
        => new PlcWebApiConfigCheck(env, PlcWebApiConfigOption.Parse(args, env.Get)).Run();

    [Fact]
    public void NoConfiguration_Passes_AsOptional()
    {
        var result = Run(new FakeEnvironmentVariableService());

        Assert.Equal("plc-webapi-config", result.Id);
        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Contains("No PLC Web API target file is configured", result.Message);
        Assert.Equal("none", result.Evidence!["source"]);
    }

    [Fact]
    public void ValidConfiguration_ReportsTargets_AndOnlyWhetherCredentialVariablesAreSet()
    {
        var env = new FakeEnvironmentVariableService();
        env.Set("L2_USER", "operator");
        env.Set("L2_PASSWORD", "very-secret");
        var path = File("""
            { "targets": [
              { "name": "Line1", "address": "192.168.0.10", "certificate": { "validation": "system" } },
              { "name": "Line2", "address": "192.168.0.11", "userEnv": "L2_USER", "passwordEnv": "L2_PASSWORD",
                "certificate": { "validation": "thumbprint", "thumbprint": "0123456789ABCDEF0123456789ABCDEF01234567" } } ] }
            """);
        env.Set(PlcWebApiConfigOption.EnvVar, path);

        var result = Run(env);

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Contains("2 PLC Web API target(s)", result.Message);
        Assert.Contains("Line2 (192.168.0.11, thumbprint)", result.Message);
        Assert.Equal("environment", result.Evidence!["source"]);
        Assert.Equal("anonymous", result.Evidence["target[0].authentication"]);
        Assert.Equal("true", result.Evidence["target[1].passwordEnvSet"]);
        Assert.Equal("L2_PASSWORD", result.Evidence["target[1].passwordEnv"]);
        Assert.DoesNotContain(result.Evidence.Values, value => value is not null && value.Contains("very-secret", StringComparison.Ordinal));
        Assert.DoesNotContain("very-secret", result.Message);
    }

    [Fact]
    public void MissingCredentialsOrDisabledValidation_Warn()
    {
        var path = File("""
            { "targets": [
              { "name": "Lab", "address": "192.168.0.30", "certificate": { "validation": "none" } },
              { "name": "Line2", "address": "192.168.0.11", "userEnv": "L2_USER", "passwordEnv": "L2_PASSWORD", "certificate": { "validation": "system" } } ] }
            """);

        var result = Run(new FakeEnvironmentVariableService(), "--plc-webapi-config", path);

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Contains("target 'Lab': certificate validation is 'none'", result.Message);
        Assert.Contains("target 'Line2': L2_USER is not set", result.Message);
        Assert.Equal("option", result.Evidence!["source"]);
    }

    [Fact]
    public void InvalidConfiguration_Fails_LikeServerStartup()
    {
        var path = File("""{ "targets": [ { "name": "A", "address": "https://x", "certificate": { "validation": "system" } } ] }""");

        var result = Run(new FakeEnvironmentVariableService(), "--plc-webapi-config=" + path);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Contains("refuses to start", result.Message);
        Assert.Contains("no scheme", result.Evidence!["error[0]"]);
        Assert.Equal(DiagnosticStatus.Failed, Run(new FakeEnvironmentVariableService(), "--plc-webapi-config", "relative.json").Status);
    }

    [Fact]
    public void DoctorParser_AcceptsTheOption_AndRejectsAMissingValue()
    {
        var path = File("""{ "targets": [] }""");
        var parsed = DoctorCliParser.Parse(new[] { "--plc-webapi-config", path, "--json" }, _ => null);
        Assert.True(parsed.Valid, parsed.ParseError);
        Assert.True(parsed.Json);
        Assert.Equal(path, parsed.PlcWebApiConfig!.Path);

        var relative = DoctorCliParser.Parse(new[] { "--plc-webapi-config", "relative.json" }, _ => null);
        Assert.True(relative.Valid, "A relative path is the check's finding, not an argument error.");
        Assert.False(relative.PlcWebApiConfig!.IsValid);

        var missing = DoctorCliParser.Parse(new[] { "--plc-webapi-config" }, _ => null);
        Assert.False(missing.Valid);
        Assert.Contains("--plc-webapi-config requires a value", missing.ParseError);
    }

    [Fact]
    public async Task Startup_RefusesAnInvalidTargetFile_AndReportsAValidOne()
    {
        var invalid = File("""{ "targets": [ { "name": "A" } ] }""");
        var refused = await StartupProcess.RunAsync(null, "--plc-webapi-config", invalid);
        Assert.Equal(1, refused.ExitCode);
        Assert.StartsWith("Error: Invalid PLC Web API configuration", refused.StandardError);
        Assert.Contains("certificate is required", refused.StandardError);

        var valid = File("""{ "targets": [ { "name": "Line1", "address": "192.168.0.10", "certificate": { "validation": "system" } } ] }""");
        var started = await StartupProcess.RunAsync(new Dictionary<string, string> { [PlcWebApiConfigOption.EnvVar] = valid });
        Assert.Equal(0, started.ExitCode);
        Assert.Contains($"TIA MCP PLC Web API targets: 1 target(s) from {valid}", started.StandardError);

        var none = await StartupProcess.RunAsync(null);
        Assert.Contains("TIA MCP PLC Web API targets: none configured", none.StandardError);
    }
}
