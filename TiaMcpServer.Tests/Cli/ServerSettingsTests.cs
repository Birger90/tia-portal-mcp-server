using System.Text.Json;
using TiaMcpServer.Cli;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Shared.PasswordManagement;
using TiaMcpServer.Diagnostics;
using TiaMcpServer.Tests.Diagnostics;
using Xunit;

namespace TiaMcpServer.Tests.Cli;

/// <summary>
/// The owner's server settings file (access mode chosen in the password manager): format, reading, the GUI logic, the
/// startup precedence CLI > environment > settings file > default, and doctor reporting. JSON fixtures are single-line or
/// built in code, so nothing depends on the checkout's line endings.
/// </summary>
[Collection("Process environment")]
public class ServerSettingsTests
{
    private const string AppData = "/appdata";
    private static readonly string DefaultPath = Path.Combine(AppData, "TiaMcpServer", "settings.json");

    // ------------------------------------------------------------------ format

    [Theory]
    [InlineData("""{ "version": 1, "accessMode": "read-only" }""", McpAccessMode.ReadOnly)]
    [InlineData("""{ "version": 1, "accessMode": "read-write" }""", McpAccessMode.ReadWrite)]
    [InlineData("""{ "accessMode": "full", "version": 1 }""", McpAccessMode.Full)]
    public void Parse_AcceptsTheCanonicalNames(string json, McpAccessMode expected)
        => Assert.Equal(expected, ServerSettingsFile.Parse(json).AccessMode);

    [Fact]
    public void Parse_VersionOnly_IsNoSetting()
        => Assert.Null(ServerSettingsFile.Parse("""{ "version": 1 }""").AccessMode);

    [Fact]
    public void Parse_DoesNotDependOnLineEndings()
    {
        var lf = "{\n  \"version\": 1,\n  \"accessMode\": \"full\"\n}\n";
        Assert.Equal(McpAccessMode.Full, ServerSettingsFile.Parse(lf).AccessMode);
        Assert.Equal(McpAccessMode.Full, ServerSettingsFile.Parse(lf.Replace("\n", "\r\n")).AccessMode);
    }

    [Theory]
    [InlineData("not json", "not valid JSON")]
    [InlineData("""[ 1 ]""", "must be a JSON object")]
    [InlineData("""{ "accessMode": "full" }""", "needs \"version\": 1 or 2")]
    [InlineData("""{ "version": "1", "accessMode": "full" }""", "needs \"version\": 1 or 2")]
    [InlineData("""{ "version": 1.5 }""", "needs \"version\": 1 or 2")]
    [InlineData("""{ "version": 3, "accessMode": "full" }""", "version 3; this server reads only versions 1 and 2")]
    [InlineData("""{ "version": 1, "claudeBridge": { } }""", "unknown member")]
    [InlineData("""{ "version": 1, "accessMode": "full", "workerTimeoutMinutes": 10 }""", "unknown member")]
    [InlineData("""{ "version": 1, "AccessMode": "full" }""", "unknown member")]
    [InlineData("""{ "version": 1, "accessMode": "full", "accessMode": "read-only" }""", "repeats the member \"accessMode\"")]
    [InlineData("""{ "version": 1, "accessMode": "FULL" }""", "must be \"read-only\", \"read-write\" or \"full\"")]
    [InlineData("""{ "version": 1, "accessMode": " full" }""", "must be \"read-only\", \"read-write\" or \"full\"")]
    [InlineData("""{ "version": 1, "accessMode": "readonly" }""", "must be \"read-only\", \"read-write\" or \"full\"")]
    [InlineData("""{ "version": 1, "accessMode": null }""", "must be \"read-only\", \"read-write\" or \"full\"")]
    [InlineData("""{ "version": 1, "accessMode": 1 }""", "must be \"read-only\", \"read-write\" or \"full\"")]
    [InlineData("""{ "version": 1, } """, "not valid JSON")]
    [InlineData("""{ "version": 1 /* note */ }""", "not valid JSON")]
    public void Parse_ClosedStrictSchema_RefusesEverythingElse(string json, string expected)
    {
        var exception = Assert.Throws<ServerSettingsException>(() => ServerSettingsFile.Parse(json));
        Assert.Contains(expected, exception.Message);
    }

    [Fact]
    public void Parse_RefusalNeverRepeatsTheUnknownMemberOrValue()
    {
        var exception = Assert.Throws<ServerSettingsException>(() => ServerSettingsFile.Parse("""{ "version": 1, "secretThing": "hunter2" }"""));
        Assert.DoesNotContain("secretThing", exception.Message);
        Assert.DoesNotContain("hunter2", exception.Message);
        Assert.Contains("version, accessMode", exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(McpAccessMode.ReadOnly)]
    [InlineData(McpAccessMode.ReadWrite)]
    [InlineData(McpAccessMode.Full)]
    public void Serialize_RoundTrips_AndWritesTheCanonicalName(McpAccessMode? mode)
    {
        var json = ServerSettingsFile.Serialize(new ServerSettings(mode));
        Assert.Equal(mode, ServerSettingsFile.Parse(json).AccessMode);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(1, document.RootElement.GetProperty("version").GetInt32());
        if (mode is { } value) Assert.Equal(McpAccessModeNames.ToName(value), document.RootElement.GetProperty("accessMode").GetString());
        else Assert.False(document.RootElement.TryGetProperty("accessMode", out _));
    }

    // ------------------------------------------------------------------ reading

    [Fact]
    public void Read_MissingDefaultFile_IsNoSetting()
    {
        var result = ServerSettingsFile.Read(_ => null, AppData, _ => false, _ => throw new InvalidOperationException("must not read"));
        Assert.True(result.IsValid);
        Assert.Null(result.Path);
        Assert.Null(result.Settings.AccessMode);
    }

    [Fact]
    public void Read_NoAppData_IsNoSetting()
        => Assert.Same(ServerSettingsReadResult.None, ServerSettingsFile.Read(_ => null, null, _ => true, _ => "{}"));

    [Fact]
    public void Read_DefaultFile_IsTheAppDataPath()
    {
        var result = ServerSettingsFile.Read(_ => null, AppData, path => path == DefaultPath, _ => """{ "version": 1, "accessMode": "full" }""");
        Assert.True(result.IsValid, result.Error);
        Assert.Equal(DefaultPath, result.Path);
        Assert.Equal(McpAccessMode.Full, result.Settings.AccessMode);
    }

    [Fact]
    public void Read_EnvironmentVariable_OverridesTheDefaultPath()
    {
        const string configured = "/elsewhere/server-settings.json";
        var read = new List<string>();
        var result = ServerSettingsFile.Read(
            name => name == ServerSettingsFile.EnvironmentVariable ? configured : null,
            AppData,
            _ => true,
            path => { read.Add(path); return """{ "version": 1, "accessMode": "read-only" }"""; });

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(configured, result.Path);
        Assert.Equal(new[] { configured }, read);
        Assert.Equal(McpAccessMode.ReadOnly, result.Settings.AccessMode);
    }

    [Fact]
    public void Read_EnvironmentVariableNamingAMissingFile_IsRefused()
    {
        var result = ServerSettingsFile.Read(name => name == ServerSettingsFile.EnvironmentVariable ? "/missing/settings.json" : null, AppData, _ => false, _ => "");
        Assert.False(result.IsValid);
        Assert.Contains("TIA_MCP_SETTINGS does not exist", result.Error);
        Assert.Contains("/missing/settings.json", result.Error);
    }

    [Fact]
    public void Read_InvalidFile_IsRefusedWithTheReasonAndThePath()
    {
        var result = ServerSettingsFile.Read(_ => null, AppData, _ => true, _ => """{ "version": 1, "accessMode": "everything" }""");
        Assert.False(result.IsValid);
        Assert.Equal(DefaultPath, result.Path);
        Assert.Null(result.Settings.AccessMode);
        Assert.Contains("\"accessMode\" must be", result.Error);
        Assert.Contains("TiaMcp Passwortverwaltung", result.Error);
        Assert.Contains(DefaultPath, result.Error);
        Assert.DoesNotContain("everything", result.Error);
    }

    [Fact]
    public void Read_UnreadableFile_IsRefused()
    {
        var result = ServerSettingsFile.Read(_ => null, AppData, _ => true, _ => throw new UnauthorizedAccessException());
        Assert.False(result.IsValid);
        Assert.Contains("could not be read (UnauthorizedAccessException", result.Error);
    }

    // ------------------------------------------------------------------ precedence

    private static Func<string, string?> Env(string? accessMode)
        => name => name == AccessModeParser.EnvVarName ? accessMode : null;

    private static ServerSettingsReadResult SettingsFile(McpAccessMode? mode)
        => ServerSettingsReadResult.Loaded(new ServerSettings(mode), DefaultPath);

    [Fact]
    public void Precedence_Default_WhenNothingIsSet()
    {
        var withoutFile = AccessModeParser.Parse(Array.Empty<string>(), Env(null), ServerSettingsReadResult.None);
        Assert.Equal(McpAccessMode.ReadWrite, withoutFile.Mode);
        Assert.Equal(AccessModeSource.Default, withoutFile.Source);
        Assert.Null(withoutFile.SettingsPath);

        var fileWithoutMode = AccessModeParser.Parse(Array.Empty<string>(), Env(null), SettingsFile(null));
        Assert.Equal(McpAccessMode.ReadWrite, fileWithoutMode.Mode);
        Assert.Equal(AccessModeSource.Default, fileWithoutMode.Source);
        Assert.Equal(DefaultPath, fileWithoutMode.SettingsPath);

        var noSettingsConsidered = AccessModeParser.Parse(Array.Empty<string>(), Env(null));
        Assert.Equal(AccessModeSource.Default, noSettingsConsidered.Source);
        Assert.Equal("default", noSettingsConsidered.DescribeSource());
    }

    [Theory]
    [InlineData(McpAccessMode.ReadOnly)]
    [InlineData(McpAccessMode.ReadWrite)]
    [InlineData(McpAccessMode.Full)]
    public void Precedence_SettingsFile_BeatsTheDefault(McpAccessMode mode)
    {
        var result = AccessModeParser.Parse(Array.Empty<string>(), Env(null), SettingsFile(mode));
        Assert.True(result.IsValid, result.Error);
        Assert.Equal(mode, result.Mode);
        Assert.Equal(AccessModeSource.SettingsFile, result.Source);
        Assert.Equal($"settingsFile ({DefaultPath})", result.DescribeSource());
    }

    [Fact]
    public void Precedence_Environment_BeatsTheSettingsFile()
    {
        var result = AccessModeParser.Parse(Array.Empty<string>(), Env("read-only"), SettingsFile(McpAccessMode.Full));
        Assert.Equal(McpAccessMode.ReadOnly, result.Mode);
        Assert.Equal(AccessModeSource.Environment, result.Source);
        Assert.Equal(DefaultPath, result.SettingsPath);
        Assert.Equal("environment (TIA_MCP_ACCESS_MODE)", result.DescribeSource());
    }

    [Theory]
    [InlineData(McpAccessMode.ReadOnly, "--read-only")]
    [InlineData(McpAccessMode.ReadWrite, "--read-write")]
    [InlineData(McpAccessMode.Full, "--access-mode", "full")]
    [InlineData(McpAccessMode.ReadOnly, "--access-mode=read-only")]
    public void Precedence_Cli_BeatsEnvironmentAndSettingsFile(McpAccessMode expected, params string[] args)
    {
        var fileMode = expected == McpAccessMode.Full ? McpAccessMode.ReadOnly : McpAccessMode.Full;
        var result = AccessModeParser.Parse(args, Env("read-write" == McpAccessModeNames.ToName(expected) ? "full" : "read-write"), SettingsFile(fileMode));
        Assert.True(result.IsValid, result.Error);
        Assert.Equal(expected, result.Mode);
        Assert.Equal(AccessModeSource.Cli, result.Source);
        Assert.Equal("cli", result.DescribeSource());
    }

    [Fact]
    public void Conflicts_ConflictingCliArgumentsStillFail_WhateverTheSettingsFileSays()
    {
        var result = AccessModeParser.Parse(new[] { "--read-only", "--access-mode=full" }, Env(null), SettingsFile(McpAccessMode.Full));
        Assert.False(result.IsValid);
        Assert.Contains("Conflicting access mode arguments", result.Error);
    }

    [Fact]
    public void Conflicts_InvalidEnvironmentStillFails_EvenWhenTheSettingsFileIsValid()
    {
        var result = AccessModeParser.Parse(Array.Empty<string>(), Env("everything"), SettingsFile(McpAccessMode.ReadOnly));
        Assert.False(result.IsValid);
        Assert.Contains("Invalid TIA_MCP_ACCESS_MODE value", result.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidSettingsFile_RefusesStartup_EvenWhenArgumentOrEnvironmentDecides(bool withArgument)
    {
        var args = withArgument ? new[] { "--access-mode", "full" } : Array.Empty<string>();
        var refused = ServerSettingsReadResult.Failed("The server settings file is not valid JSON. (file: x)", "x");
        Assert.False(AccessModeParser.Parse(args, Env(null), refused).IsValid);
        var withEnvironment = AccessModeParser.Parse(args, Env("read-only"), refused);
        Assert.False(withEnvironment.IsValid);
        Assert.Equal(refused.Error, withEnvironment.Error);
    }

    [Fact]
    public void SourceNames_AreCanonical()
    {
        Assert.Equal("default", AccessModeParseResult.SourceName(AccessModeSource.Default));
        Assert.Equal("cli", AccessModeParseResult.SourceName(AccessModeSource.Cli));
        Assert.Equal("environment", AccessModeParseResult.SourceName(AccessModeSource.Environment));
        Assert.Equal("settingsFile", AccessModeParseResult.SourceName(AccessModeSource.SettingsFile));
    }

    // ------------------------------------------------------------------ doctor

    [Fact]
    public void DoctorParser_UsesTheSamePrecedence()
    {
        var fromFile = DoctorCliParser.Parse(Array.Empty<string>(), Env(null), SettingsFile(McpAccessMode.Full));
        Assert.True(fromFile.Valid, fromFile.ParseError);
        Assert.Equal(McpAccessMode.Full, fromFile.AccessMode);
        Assert.Equal(AccessModeSource.SettingsFile, fromFile.AccessModeResolution!.Source);

        var fromCli = DoctorCliParser.Parse(new[] { "--read-only" }, Env("read-write"), SettingsFile(McpAccessMode.Full));
        Assert.Equal(McpAccessMode.ReadOnly, fromCli.AccessMode);
        Assert.Equal(AccessModeSource.Cli, fromCli.AccessModeResolution!.Source);

        var refused = DoctorCliParser.Parse(Array.Empty<string>(), Env(null), ServerSettingsReadResult.Failed("broken settings (file: x)", "x"));
        Assert.False(refused.Valid);
        Assert.Equal("broken settings (file: x)", refused.ParseError);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Doctor_ReportsTheAccessModeSourceAndTheSettingsFile(bool json)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var args = json ? new[] { "--json" } : Array.Empty<string>();

        var exit = await DoctorCommand.RunAsync(args, _ => PassedReport(), output, error, () => SettingsFile(McpAccessMode.ReadOnly));

        Assert.Equal(0, exit);
        Assert.Empty(error.ToString());
        if (json)
        {
            using var document = JsonDocument.Parse(output.ToString());
            var root = document.RootElement;
            Assert.Equal("read-only", root.GetProperty("accessMode").GetString());
            Assert.Equal("settingsFile", root.GetProperty("accessModeSource").GetString());
            Assert.Equal(DefaultPath, root.GetProperty("settingsFile").GetString());
            var names = root.EnumerateObject().Select(property => property.Name).ToArray();
            var index = Array.IndexOf(names, "accessMode");
            Assert.Equal(new[] { "accessMode", "accessModeSource", "settingsFile", "workerTimeoutMinutes", "workerTimeoutSource" }, names.Skip(index).Take(5));
        }
        else
        {
            var text = output.ToString();
            Assert.Contains($"Access mode: READ-ONLY (settingsFile){Environment.NewLine}Settings file: {DefaultPath}{Environment.NewLine}", text);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Doctor_WithoutSettingsFile_ReportsDefaultAndNoFile(bool json)
    {
        var output = new StringWriter();
        var args = json ? new[] { "--json" } : Array.Empty<string>();

        var exit = await DoctorCommand.RunAsync(args, _ => PassedReport(), output, new StringWriter(), () => ServerSettingsReadResult.None);

        Assert.Equal(0, exit);
        if (json)
        {
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Equal("default", document.RootElement.GetProperty("accessModeSource").GetString());
            Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("settingsFile").ValueKind);
        }
        else
        {
            Assert.Contains($"Access mode: READ-WRITE (default){Environment.NewLine}Settings file: none{Environment.NewLine}", output.ToString());
        }
    }

    [Fact]
    public async Task Doctor_InvalidSettingsFile_IsAnArgumentError()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = await DoctorCommand.RunAsync(Array.Empty<string>(), _ => throw new InvalidOperationException("Checks must not run."), output, error,
            () => ServerSettingsReadResult.Failed("The server settings file is not valid JSON. (file: x)", "x"));

        Assert.Equal(2, exit);
        Assert.StartsWith("error: The server settings file is not valid JSON. (file: x)", error.ToString());
    }

    // ------------------------------------------------------------------ password manager logic

    [Fact]
    public void Editor_MissingFile_IsNotSet_AndSaveCreatesIt()
    {
        var directory = TemporaryDirectory();
        var path = Path.Combine(directory, "TiaMcpServer", "settings.json");
        try
        {
            var editor = ServerSettingsEditor.Load(path);
            Assert.True(editor.CanSave);
            Assert.False(editor.FileExists);
            Assert.Null(editor.CurrentMode);
            Assert.Equal("nicht gesetzt – Standard read-write", editor.CurrentDescription);

            Assert.Null(editor.Save(McpAccessMode.Full));
            Assert.True(editor.FileExists);
            Assert.Equal(McpAccessMode.Full, editor.CurrentMode);
            Assert.Equal("Voll, ohne Rückfrage (full)", editor.CurrentDescription);
            Assert.Contains("Neustart", editor.SavedMessage());
            Assert.Equal(McpAccessMode.Full, ServerSettingsFile.Parse(System.IO.File.ReadAllText(path)).AccessMode);

            // What the server would read from that file.
            var read = ServerSettingsFile.Read(name => name == ServerSettingsFile.EnvironmentVariable ? path : null, null);
            Assert.True(read.IsValid, read.Error);
            Assert.Equal(AccessModeSource.SettingsFile, AccessModeParser.Parse(Array.Empty<string>(), Env(null), read).Source);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Editor_ReplacesTheFileAtomically_AndLeavesNoTemporaryFile()
    {
        var directory = TemporaryDirectory();
        var path = Path.Combine(directory, "settings.json");
        try
        {
            foreach (var mode in new McpAccessMode?[] { McpAccessMode.ReadOnly, McpAccessMode.ReadWrite, McpAccessMode.Full, null })
            {
                var editor = ServerSettingsEditor.Load(path);
                Assert.Null(editor.Save(mode));
                Assert.Equal(mode, ServerSettingsEditor.Load(path).CurrentMode);
                Assert.Equal(new[] { path }, Directory.GetFiles(directory));
            }

            Assert.Equal("nicht gesetzt – Standard read-write", ServerSettingsEditor.Load(path).CurrentDescription);
            Assert.True(ServerSettingsEditor.Load(path).FileExists);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Editor_FailedWrite_ReportsTheError_AndRemovesTheTemporaryFile()
    {
        var directory = TemporaryDirectory();
        // The target is a directory: the temporary file is written, the move into place fails.
        var path = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(path);
        try
        {
            var error = ServerSettingsEditor.Load(path, _ => false, _ => "").Save(McpAccessMode.Full);
            Assert.NotNull(error);
            Assert.Contains("konnte nicht geschrieben werden", error);
            Assert.True(Directory.Exists(path));
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Editor_InvalidFile_IsNeverOverwritten()
    {
        var directory = TemporaryDirectory();
        var path = Path.Combine(directory, "settings.json");
        const string futureFile = """{ "version": 3, "accessMode": "full", "somethingNew": true }""";
        System.IO.File.WriteAllText(path, futureFile);
        try
        {
            var editor = ServerSettingsEditor.Load(path);
            Assert.False(editor.CanSave);
            Assert.Null(editor.CurrentMode);
            Assert.Contains("version 3", editor.LoadError);
            Assert.Contains("ungültig", editor.CurrentDescription);
            Assert.NotNull(editor.Save(McpAccessMode.ReadOnly));
            Assert.Equal(futureFile, System.IO.File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Editor_UnreadableFile_CannotBeSaved()
    {
        var editor = ServerSettingsEditor.Load("/x/settings.json", _ => true, _ => throw new IOException("locked"));
        Assert.False(editor.CanSave);
        Assert.Contains("IOException", editor.LoadError);
    }

    [Fact]
    public void Editor_RefusesDevicePaths()
        => Assert.Throws<IOException>(() => ServerSettingsEditor.WriteFile(@"\\?\C:\x\settings.json", ServerSettings.Empty));

    [Fact]
    public void Editor_Texts_NameAllThreeModesTheRestartAndThePrecedence()
    {
        Assert.Equal(new[] { McpAccessMode.ReadOnly, McpAccessMode.ReadWrite, McpAccessMode.Full }, ServerSettingsEditor.Choices.Select(c => c.Mode));
        Assert.Equal(new[] { "Nur lesen (read-only)", "Lesen und schreiben, mit Rückfrage (read-write)", "Voll, ohne Rückfrage (full)" },
            ServerSettingsEditor.Choices.Select(c => c.Label));
        Assert.All(ServerSettingsEditor.Choices, choice => Assert.False(string.IsNullOrWhiteSpace(choice.Explanation)));
        Assert.Contains("--access-mode", ServerSettingsEditor.PrecedenceNotice);
        Assert.Contains("TIA_MCP_ACCESS_MODE", ServerSettingsEditor.PrecedenceNotice);
        Assert.Contains("Claude Desktop", ServerSettingsEditor.PrecedenceNotice);
        Assert.Contains("Neustart", ServerSettingsEditor.PrecedenceNotice);
        Assert.Equal("Zugriffsmodus", ServerSettingsEditor.SectionTitle);
    }

    [Fact]
    public void Options_SettingsPathFromArgumentThenEnvironmentThenAppData()
    {
        var env = new Dictionary<string, string?> { [ServerSettingsFile.EnvironmentVariable] = @"\\server\share\settings.json" };
        Assert.Equal(@"C:\s.json", PasswordManagerOptions.Parse(new[] { "--settings", @"C:\s.json" }, n => env.GetValueOrDefault(n), AppData).SettingsPath);
        Assert.Equal(@"C:\s.json", PasswordManagerOptions.Parse(new[] { @"--settings=C:\s.json" }, n => env.GetValueOrDefault(n), AppData).SettingsPath);
        Assert.Equal(@"\\server\share\settings.json", PasswordManagerOptions.Parse(Array.Empty<string>(), n => env.GetValueOrDefault(n), AppData).SettingsPath);
        Assert.Equal(DefaultPath, PasswordManagerOptions.Parse(Array.Empty<string>(), _ => null, AppData).SettingsPath);
        Assert.Contains("braucht einen Pfad", PasswordManagerOptions.Parse(new[] { "--settings=" }, _ => null, AppData).Error);
        Assert.Contains("--settings", PasswordManagerOptions.Usage);
        Assert.Contains("--settings", PasswordManagerCommand.UsageText);
        Assert.Contains("Zugriffsmodus", PasswordManagerCommand.UsageText);
    }

    // ------------------------------------------------------------------ the server never writes the file

    [Fact]
    public void ServerSource_NeverWritesTheSettingsFile()
    {
        var host = Path.Combine(ReferenceStubArtifactTests.RepositoryRoot, "TiaMcpServer");
        var offenders = Directory.EnumerateFiles(host, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(file => System.IO.File.ReadAllText(file) is var text && (text.Contains("ServerSettingsEditor") || text.Contains("ServerSettingsFile.Serialize")))
            .ToArray();
        Assert.Empty(offenders);
    }

    private static string TemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tiamcp-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static DoctorReport PassedReport()
        => new(DiagnosticStatus.Passed, DateTimeOffset.UtcNow, "1.0.0", new DoctorSummary(0, 0, 0), Array.Empty<DiagnosticCheckResult>());
}
