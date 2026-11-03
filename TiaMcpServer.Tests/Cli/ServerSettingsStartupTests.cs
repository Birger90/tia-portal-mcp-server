using TiaMcpServer.Contracts.Shared;
using Xunit;

namespace TiaMcpServer.Tests.Cli;

/// <summary>The real host process reads the settings file named by TIA_MCP_SETTINGS and reports the access mode source.</summary>
public class ServerSettingsStartupTests
{
    [Fact]
    public async Task SettingsFile_SetsTheAccessMode_AndIsReported()
    {
        var path = WriteSettings("{ \"version\": 1, \"accessMode\": \"full\" }");
        try
        {
            var result = await StartupProcess.RunAsync(new Dictionary<string, string> { [ServerSettingsFile.EnvironmentVariable] = path });

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("TIA MCP access mode: FULL", result.StandardError);
            Assert.Contains($"TIA MCP access mode source: settingsFile ({path})", result.StandardError);
            Assert.Contains($"TIA MCP settings file: {path}", result.StandardError);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CliArgument_TakesPrecedenceOverTheSettingsFile()
    {
        var path = WriteSettings("{ \"version\": 1, \"accessMode\": \"full\" }");
        try
        {
            var result = await StartupProcess.RunAsync(new Dictionary<string, string> { [ServerSettingsFile.EnvironmentVariable] = path }, "--read-only");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("TIA MCP access mode: READ-ONLY", result.StandardError);
            Assert.Contains("TIA MCP access mode source: cli", result.StandardError);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task EnvironmentVariable_TakesPrecedenceOverTheSettingsFile()
    {
        var path = WriteSettings("{ \"version\": 1, \"accessMode\": \"full\" }");
        try
        {
            var result = await StartupProcess.RunAsync(new Dictionary<string, string>
            {
                [ServerSettingsFile.EnvironmentVariable] = path,
                ["TIA_MCP_ACCESS_MODE"] = "read-only"
            });

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("TIA MCP access mode: READ-ONLY", result.StandardError);
            Assert.Contains("TIA MCP access mode source: environment (TIA_MCP_ACCESS_MODE)", result.StandardError);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task NoAccessModeInTheFile_UsesTheDefault()
    {
        var result = await StartupProcess.RunAsync(null);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("TIA MCP access mode: READ-WRITE", result.StandardError);
        Assert.Contains("TIA MCP access mode source: default", result.StandardError);
    }

    [Fact]
    public async Task InvalidSettingsFile_RefusesStartup()
    {
        var path = WriteSettings("{ \"version\": 1, \"accessMode\": \"everything\" }");
        try
        {
            var result = await StartupProcess.RunAsync(new Dictionary<string, string> { [ServerSettingsFile.EnvironmentVariable] = path }, "--read-only");

            Assert.Equal(1, result.ExitCode);
            Assert.StartsWith("Error: The server settings file: \"accessMode\" must be", result.StandardError);
            Assert.Contains(path, result.StandardError);
            Assert.Empty(result.StandardOutput);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task MissingFileNamedByTheVariable_RefusesStartup()
    {
        var path = Path.Combine(Path.GetTempPath(), "tiamcp-missing-" + Guid.NewGuid().ToString("N") + ".json");

        var result = await StartupProcess.RunAsync(new Dictionary<string, string> { [ServerSettingsFile.EnvironmentVariable] = path });

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("TIA_MCP_SETTINGS does not exist", result.StandardError);
    }

    private static string WriteSettings(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), "tiamcp-settings-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        return path;
    }
}
