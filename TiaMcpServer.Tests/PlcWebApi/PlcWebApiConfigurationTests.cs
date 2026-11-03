using TiaMcpServer.PlcWebApi;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.PlcWebApi;

public sealed class PlcWebApiConfigurationTests
{
    private const string Valid = """
        {
          "targets": [
            { "name": "Line1", "address": "192.168.0.10", "certificate": { "validation": "system" } },
            { "name": "Secure", "address": "plc-2.plant.local:8443", "userEnv": "LINE2_USER", "passwordEnv": "LINE2_PASSWORD",
              "certificate": { "validation": "thumbprint", "thumbprint": "ab:cd ef0123456789abcdef0123456789abcdef01" }, "requestTimeoutSeconds": 90 },
            { "name": "Lab", "address": "[fd00::10]", "certificate": { "validation": "none" } }
          ]
        }
        """;

    [Fact]
    public void Parse_AcceptsTheDocumentedShape_AndNormalizes()
    {
        var result = PlcWebApiConfigLoader.Parse("/etc/plc.json", Valid);

        Assert.True(result.IsValid, string.Join("\n", result.Errors));
        var targets = result.Configuration!.Targets;
        Assert.Equal(new[] { "Line1", "Secure", "Lab" }, targets.Select(target => target.Name));
        Assert.True(targets[0].IsAnonymous);
        Assert.Equal(TimeSpan.FromSeconds(30), targets[0].RequestTimeout);
        Assert.Equal(PlcWebApiCertificateValidation.System, targets[0].CertificateValidation);
        Assert.Equal("LINE2_USER", targets[1].UserEnv);
        Assert.Equal("ABCDEF0123456789ABCDEF0123456789ABCDEF01", targets[1].Thumbprint);
        Assert.Equal(TimeSpan.FromSeconds(90), targets[1].RequestTimeout);
        Assert.Equal("plc-2.plant.local:8443", targets[1].Address);
        Assert.Equal(PlcWebApiCertificateValidation.None, targets[2].CertificateValidation);
        Assert.Same(targets[1], result.Configuration.Find("Secure"));
        Assert.Null(result.Configuration.Find("secure"));
        Assert.Equal("/etc/plc.json", result.Configuration.SourcePath);
    }

    [Theory]
    [InlineData("""{ "targets": [ { "name": "A", "address": "1.2.3.4", "certificate": { "validation": "system" }, "password": "x" } ] }""", "not valid JSON for its schema")]
    [InlineData("""{ "targets": [ { "name": "A", "address": "1.2.3.4" } ] }""", "certificate is required")]
    [InlineData("""{ "targets": [ { "name": "A", "address": "1.2.3.4", "certificate": { "validation": "strict" } } ] }""", "validation must be one of")]
    [InlineData("""{ "targets": [ { "name": "A", "address": "1.2.3.4", "certificate": { "validation": "thumbprint" } } ] }""", "thumbprint must be the SHA-1")]
    [InlineData("""{ "targets": [ { "name": "A", "address": "1.2.3.4", "certificate": { "validation": "thumbprint", "thumbprint": "12" } } ] }""", "thumbprint must be the SHA-1")]
    [InlineData("""{ "targets": [ { "name": "A", "address": "1.2.3.4", "certificate": { "validation": "system", "thumbprint": "AB" } } ] }""", "only valid with validation 'thumbprint'")]
    [InlineData("""{ "targets": [ { "name": "A", "address": "https://1.2.3.4", "certificate": { "validation": "system" } } ] }""", "no scheme")]
    [InlineData("""{ "targets": [ { "name": "A", "address": "1.2.3.4/api", "certificate": { "validation": "system" } } ] }""", "no scheme, path")]
    [InlineData("""{ "targets": [ { "name": "A", "address": "user@1.2.3.4", "certificate": { "validation": "system" } } ] }""", "no scheme, path")]
    [InlineData("""{ "targets": [ { "name": "A", "address": " 1.2.3.4", "certificate": { "validation": "system" } } ] }""", "without surrounding spaces")]
    [InlineData("""{ "targets": [ { "name": "A", "address": "1.2.3.4", "userEnv": "U", "certificate": { "validation": "system" } } ] }""", "must be given together")]
    [InlineData("""{ "targets": [ { "name": "A", "address": "1.2.3.4", "userEnv": "1U", "passwordEnv": "P", "certificate": { "validation": "system" } } ] }""", "environment variable name")]
    [InlineData("""{ "targets": [ { "name": "A", "address": "1.2.3.4", "certificate": { "validation": "system" }, "requestTimeoutSeconds": 0 } ] }""", "requestTimeoutSeconds must be 1 to 600")]
    [InlineData("""{ "targets": [ { "name": "A", "address": "1.2.3.4", "certificate": { "validation": "system" } }, { "name": "A", "address": "1.2.3.5", "certificate": { "validation": "system" } } ] }""", "used by another target")]
    [InlineData("""{ "targets": [ { "name": " A", "address": "1.2.3.4", "certificate": { "validation": "system" } } ] }""", "name must be 1 to 64")]
    [InlineData("""{ "targets": [ { "Name": "A", "address": "1.2.3.4", "certificate": { "validation": "system" } } ] }""", "not valid JSON for its schema")]
    [InlineData("""{ /* comment */ "targets": [] }""", "not valid JSON for its schema")]
    [InlineData("""{ }""", "not valid JSON for its schema")]
    public void Parse_RejectsEveryInvalidShape_WithoutEchoingValues(string json, string expected)
    {
        var result = PlcWebApiConfigLoader.Parse("/etc/plc.json", json);

        Assert.False(result.IsValid);
        Assert.Null(result.Configuration);
        Assert.Contains(result.Errors, error => error.Contains(expected, StringComparison.Ordinal));
        Assert.DoesNotContain(result.Errors, error => error.Contains("\"password\": \"x\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_AcceptsAnEmptyTargetList()
    {
        var result = PlcWebApiConfigLoader.Parse("/etc/plc.json", """{ "targets": [] }""");
        Assert.True(result.IsValid);
        Assert.Empty(result.Configuration!.Targets);
    }

    [Fact]
    public void Load_RequiresAnAbsoluteExistingFile()
    {
        using var directory = new TempAuditDirectory();
        Directory.CreateDirectory(directory.Path);
        Assert.Contains("must be absolute", Assert.Single(PlcWebApiConfigLoader.Load("relative.json").Errors));
        Assert.Contains("does not exist", Assert.Single(PlcWebApiConfigLoader.Load(Path.Combine(directory.Path, "missing.json")).Errors));
        var path = Path.Combine(directory.Path, "plc.json");
        File.WriteAllText(path, Valid);
        var loaded = PlcWebApiConfigLoader.Load(path);
        Assert.True(loaded.IsValid);
        Assert.Equal(path, loaded.Configuration!.SourcePath);
    }

    [Fact]
    public void Option_PrefersTheCommandLine_ThenTheEnvironment()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "plc.json");
        string? Env(string name) => name == PlcWebApiConfigOption.EnvVar ? Path.Combine(Path.GetTempPath(), "env.json") : null;

        var fromOption = PlcWebApiConfigOption.Parse(new[] { "--plc-webapi-config", absolute }, Env);
        Assert.True(fromOption.IsValid);
        Assert.Equal(absolute, fromOption.Path);
        Assert.Equal(PlcWebApiConfigSource.Option, fromOption.Source);
        Assert.Equal(absolute, PlcWebApiConfigOption.Parse(new[] { "--plc-webapi-config=" + absolute }, Env).Path);

        var fromEnvironment = PlcWebApiConfigOption.Parse(Array.Empty<string>(), Env);
        Assert.Equal(PlcWebApiConfigSource.Environment, fromEnvironment.Source);
        Assert.EndsWith("env.json", fromEnvironment.Path);

        var none = PlcWebApiConfigOption.Parse(Array.Empty<string>(), _ => null);
        Assert.True(none.IsValid);
        Assert.Null(none.Path);
        Assert.Equal("none", PlcWebApiConfigOptionResult.SourceName(none.Source));
        Assert.Same(PlcWebApiConfiguration.Empty, PlcWebApiConfigOption.Resolve(Array.Empty<string>(), _ => null).Configuration);
    }

    [Theory]
    [InlineData("--plc-webapi-config")]
    [InlineData("--plc-webapi-config", "relative.json")]
    [InlineData("--plc-webapi-config", "/a.json", "--plc-webapi-config", "/b.json")]
    public void Option_RejectsMissingRelativeOrRepeatedValues(params string[] args)
    {
        var result = PlcWebApiConfigOption.Parse(args, _ => null);
        Assert.False(result.IsValid);
        Assert.Contains("--plc-webapi-config", result.Error);
        var (configuration, error) = PlcWebApiConfigOption.Resolve(args, _ => null);
        Assert.Null(configuration);
        Assert.NotNull(error);
    }

    [Fact]
    public void Resolve_ReportsEveryProblemOfAnInvalidFile()
    {
        using var directory = new TempAuditDirectory();
        Directory.CreateDirectory(directory.Path);
        var path = Path.Combine(directory.Path, "plc.json");
        File.WriteAllText(path, """{ "targets": [ { "name": "A", "address": "https://x" } ] }""");

        var (configuration, error) = PlcWebApiConfigOption.Resolve(new[] { "--plc-webapi-config", path }, _ => null);

        Assert.Null(configuration);
        Assert.Contains(path, error);
        Assert.Contains("no scheme", error);
        Assert.Contains("certificate is required", error);
    }
}
