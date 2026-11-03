using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.PlcDownload;
using Xunit;

namespace TiaMcpServer.Tests.PlcDownload;

/// <summary>
/// The shared password-source rule (environment variable NAME or Windows Credential Manager generic credential), its JSON
/// argument, the names-only password map file and the off-Windows refusal. No secret is ever read from a real store here.
/// </summary>
[Collection("Mcp protocol serial")]
public sealed class PasswordSourceTests
{
    // ------------------------------------------------------------------ owner-set credential prefix

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("TiaMcp/")]
    [InlineData("Plant/Line 1/")]
    [InlineData("*")]
    public void CredentialPrefix_AcceptsOwnerValues(string? value) => Assert.Null(PasswordSources.CredentialPrefixError(value));

    [Theory]
    [InlineData(" TiaMcp/")]
    [InlineData("Tia*Mcp/")]
    [InlineData("Tia\tMcp/")]
    [InlineData("Tia\nMcp/")]
    public void CredentialPrefix_RefusesMalformedValues(string value)
        => Assert.Contains(PasswordSources.CredentialPrefixVariable, PasswordSources.CredentialPrefixError(value.Replace("\\t", "\t").Replace("\\n", "\n")));

    [Fact]
    public void CredentialPrefix_InvalidConfigurationIsRefused_AndTheDefaultStays()
    {
        Assert.NotNull(PasswordSources.ConfigureCredentialPrefix(_ => "Bad*Prefix"));
        Assert.Equal(PasswordSources.CredentialTargetPrefix, PasswordSources.ActiveCredentialPrefix);
        Assert.Null(PasswordSources.ConfigureCredentialPrefix(_ => null));
        Assert.Equal("TiaMcp/ (default)", PasswordSources.DescribeCredentialPrefix());
    }

    [Fact]
    public void CredentialTargets_FollowTheConfiguredPrefix()
    {
        Assert.Null(PasswordSources.CredentialTargetError("Plant/Line1/Safety", "Plant/"));
        Assert.Contains("'Plant/'", PasswordSources.CredentialTargetError("TiaMcp/Line1/Safety", "Plant/"));
        Assert.Contains("'Plant/'", PasswordSources.CredentialTargetError("Plant/", "Plant/"));
        // '*' (owner setting) accepts any generic credential target; the other target rules still apply.
        Assert.Null(PasswordSources.CredentialTargetError("git:https://example.invalid", PasswordSources.AnyCredentialPrefix));
        Assert.NotNull(PasswordSources.CredentialTargetError("bad\ttarget".Replace("\\t", "\t"), PasswordSources.AnyCredentialPrefix));
        Assert.NotNull(PasswordSources.CredentialTargetError("", PasswordSources.AnyCredentialPrefix));
    }

    [Fact]
    public void Doctor_ReportsTheActivePrefix_AndFailsAnInvalidOne()
    {
        var unset = new TiaMcpServer.Diagnostics.Checks.PasswordSourcesCheck(new Env(new())).Run();
        Assert.Equal(TiaMcpServer.Diagnostics.DiagnosticStatus.Passed, unset.Status);
        Assert.Equal("TiaMcp/", unset.Evidence!["credentialPrefix"]);
        var any = new TiaMcpServer.Diagnostics.Checks.PasswordSourcesCheck(new Env(new() { [PasswordSources.CredentialPrefixVariable] = "*" })).Run();
        Assert.Contains("every Windows generic credential", any.Message);
        var bad = new TiaMcpServer.Diagnostics.Checks.PasswordSourcesCheck(new Env(new() { [PasswordSources.CredentialPrefixVariable] = "a*b" })).Run();
        Assert.Equal(TiaMcpServer.Diagnostics.DiagnosticStatus.Failed, bad.Status);
    }

    private sealed class Env(Dictionary<string, string> values) : TiaMcpServer.Diagnostics.IEnvironmentVariableService
    {
        public string? Get(string name) => values.TryGetValue(name, out var value) ? value : null;
    }

    private sealed class FakeCredentials(Dictionary<string, string> store) : ICredentialReader
    {
        public List<string> Requested { get; } = new();

        public SecureString? Read(string target, out string? reason)
        {
            Requested.Add(target);
            if (!store.TryGetValue(target, out var value))
            {
                reason = "not found";
                return null;
            }

            var secure = new SecureString();
            foreach (var character in value) secure.AppendChar(character);
            secure.MakeReadOnly();
            reason = null;
            return secure;
        }
    }

    [Theory]
    [InlineData("PLC_PW", PasswordSourceKind.Environment)]
    [InlineData("credential:TiaMcp/MCP_Safety_01/PLC_F1/Safety", PasswordSourceKind.Credential)]
    [InlineData("credential:TiaMcp/with space", PasswordSourceKind.Credential)]
    [InlineData("credential:Other/PLC", PasswordSourceKind.Invalid)]
    [InlineData("credential:TiaMcp/", PasswordSourceKind.Invalid)]
    [InlineData("credential:TiaMcp/a*b", PasswordSourceKind.Invalid)]
    [InlineData("credential:TiaMcp/a\tb", PasswordSourceKind.Invalid)]
    [InlineData("Secr3t pass!", PasswordSourceKind.Invalid)]
    [InlineData("9LIVES", PasswordSourceKind.Invalid)]
    [InlineData(null, PasswordSourceKind.Invalid)]
    public void KindOf_AcceptsEnvironmentNamesAndTiaMcpCredentialTargetsOnly(string? token, PasswordSourceKind expected)
        => Assert.Equal(expected, PasswordSources.KindOf(token));

    [Fact]
    public void CredentialTargets_AreBoundedAndNeverRepeatedInRefusals()
    {
        Assert.Null(PasswordSources.CredentialTargetError("TiaMcp/" + new string('a', PasswordSources.MaxCredentialTargetLength - 7)));
        Assert.NotNull(PasswordSources.CredentialTargetError("TiaMcp/" + new string('a', PasswordSources.MaxCredentialTargetLength)));
        Assert.DoesNotContain("Secret-Target", PasswordSources.CredentialTargetError("Secret-Target"));
        Assert.Equal("credential", PasswordSources.KindName("credential:TiaMcp/x"));
        Assert.Equal("environment", PasswordSources.KindName("PLC_PW"));
    }

    [Fact]
    public void Resolve_ReadsEnvironmentOrCredential_IntoAReadOnlySecureString()
    {
        var credentials = new FakeCredentials(new() { ["TiaMcp/P/PLC_1/Safety"] = "c-secret" });
        using (var fromEnvironment = PasswordSources.Resolve("PLC_PW", name => name == "PLC_PW" ? "e-secret" : null, credentials, out var reason))
        {
            Assert.Null(reason);
            Assert.Equal(8, fromEnvironment!.Length);
            Assert.True(fromEnvironment.IsReadOnly());
        }

        using (var fromCredential = PasswordSources.Resolve("credential:TiaMcp/P/PLC_1/Safety", _ => null, credentials, out var reason))
        {
            Assert.Null(reason);
            Assert.Equal(8, fromCredential!.Length);
        }

        Assert.Null(PasswordSources.Resolve("UNSET_PW", _ => null, credentials, out var unset));
        Assert.Contains("not set", unset);
        Assert.Null(PasswordSources.Resolve("credential:TiaMcp/missing", _ => null, credentials, out var missing));
        Assert.Equal("not found", missing);
        Assert.Null(PasswordSources.Resolve("credential:Other/x", _ => "x", credentials, out var invalid));
        Assert.Contains("TiaMcp/", invalid);
        // An invalid token never reaches the store.
        Assert.Equal(new[] { "TiaMcp/P/PLC_1/Safety", "TiaMcp/missing" }, credentials.Requested);
    }

    [Fact]
    public void WindowsCredentialReader_RefusesClearlyOffWindows()
    {
        var secure = WindowsCredentialReader.Instance.Read("TiaMcp/does-not-exist/" + Guid.NewGuid().ToString("N"), out var reason);
        Assert.Null(secure);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Assert.Contains("cmdkey", reason);
        else
            Assert.Equal(WindowsCredentialReader.UnsupportedReason, reason);
        // The download configuration path reports the same reason as password_unavailable.
        Assert.Null(PasswordSources.Resolve("credential:TiaMcp/x", _ => null, null, out var viaResolver));
        Assert.False(string.IsNullOrEmpty(viaResolver));
    }

    // ------------------------------------------------------------------ JSON argument

    private static string? Bound(string json)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();
        return PasswordSourceArgument.Read(ref reader, name => name == "SET_PW" ? "x" : null);
    }

    [Theory]
    [InlineData("\"SET_PW\"", "SET_PW")]
    [InlineData("{\"env\":\"SET_PW\"}", "SET_PW")]
    [InlineData("{\"env\":\"UNSET_PW\"}", PlcDownloadPasswordMap.UnsetVariable)]
    [InlineData("\"pasted password!\"", PlcDownloadPasswordMap.RedactedVariable)]
    [InlineData("{\"credential\":\"TiaMcp/P/PLC_1/Safety\"}", "credential:TiaMcp/P/PLC_1/Safety")]
    [InlineData("{\"credential\":\"git:https://github.com\"}", PlcDownloadPasswordMap.RedactedVariable)]
    public void Argument_BindsEveryFormToOneRedactedToken(string json, string expected) => Assert.Equal(expected, Bound(json));

    [Theory]
    [InlineData("42")]
    [InlineData("{}")]
    [InlineData("{\"value\":\"secret\"}")]
    [InlineData("{\"env\":\"SET_PW\",\"credential\":\"TiaMcp/x\"}")]
    [InlineData("{\"credential\":7}")]
    public void Argument_RefusesOtherShapes_WithoutEchoingValues(string json)
    {
        var refused = Assert.Throws<JsonException>(() => Bound(json));
        Assert.DoesNotContain("secret", refused.Message);
    }

    [Fact]
    public void PasswordsMap_AcceptsMixedSources_AndRoundTripsThem()
    {
        Environment.SetEnvironmentVariable("TIA_MCP_TEST_SOURCE_PW", "x");
        try
        {
            var map = JsonSerializer.Deserialize<PlcDownloadPasswordMap>(
                "{\"ModuleWriteAccessPassword\":\"TIA_MCP_TEST_SOURCE_PW\",\"ModuleReadAccessPassword\":{\"credential\":\"TiaMcp/P/PLC_1/Read\"}}")!;
            Assert.Equal(new[] { "TIA_MCP_TEST_SOURCE_PW", "credential:TiaMcp/P/PLC_1/Read" }, map.Entries.Select(e => e.Variable));
            var written = JsonSerializer.Serialize(map);
            Assert.Contains("{\"credential\":\"TiaMcp/P/PLC_1/Read\"}", written);
            Assert.Equal(map.Entries.Select(e => e.Variable), JsonSerializer.Deserialize<PlcDownloadPasswordMap>(written)!.Entries.Select(e => e.Variable));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PlcDownloadPasswordMap>("{\"A\":\"TIA_MCP_TEST_SOURCE_PW\",\"A\":\"TIA_MCP_TEST_SOURCE_PW\"}"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIA_MCP_TEST_SOURCE_PW", null);
        }
    }

    [Fact]
    public void Policies_AcceptCredentialTokensForDownloadOnlineAndUpload()
    {
        Assert.Null(PlcDownloadPolicy.ValidateDecisions(Array.Empty<PlcDownloadDecisionInfo>(), new[] { new PlcDownloadPasswordInfo { Type = "ModuleWriteAccessPassword", Variable = "credential:TiaMcp/x" } }));
        Assert.NotNull(PlcDownloadPolicy.ValidateDecisions(Array.Empty<PlcDownloadDecisionInfo>(), new[] { new PlcDownloadPasswordInfo { Type = "ModuleWriteAccessPassword", Variable = "credential:Other/x" } }));
        Assert.Null(PlcCallbackPolicy.ValidateEntries(Array.Empty<PlcCallbackDecisionInfo>(), new[] { new PlcCallbackPasswordInfo { Type = "OnlinePasswordConfiguration", Variable = "credential:TiaMcp/x" } }));
        Assert.NotNull(PlcCallbackPolicy.ValidateEntries(Array.Empty<PlcCallbackDecisionInfo>(), new[] { new PlcCallbackPasswordInfo { Type = "OnlinePasswordConfiguration", Variable = "pass word" } }));
    }

    [Fact]
    public void Schema_OffersTheThreeForms()
    {
        var schema = PasswordSourceArgument.Schema(nullable: true).ToJsonString();
        Assert.Contains("\"env\"", schema);
        Assert.Contains("\"credential\"", schema);
        Assert.Contains("^TiaMcp/", schema);
        Assert.Contains("\"null\"", schema);
        Assert.DoesNotContain("\"null\"", PasswordSourceArgument.Schema(nullable: false).ToJsonString());
    }

    // ------------------------------------------------------------------ password map file

    private const string ValidMap = """
        { "version": 1, "entries": [
          { "project": "MCP_Safety_01", "plc": "PLC_F1", "purpose": "safety", "credential": "TiaMcp/MCP_Safety_01/PLC_F1/Safety" },
          { "plc": "PLC_F1", "purpose": "safety", "env": "F1_SAFETY_PW" },
          { "purpose": "safety", "env": "ANY_SAFETY_PW" },
          { "plc": "PLC_1", "purpose": "ModuleWriteAccessPassword", "env": "PLC1_WRITE_PW" } ] }
        """;

    [Fact]
    public void MapFile_ParsesNamesAndPicksTheMostSpecificEntry()
    {
        var entries = PasswordMapFile.Parse(ValidMap);
        Assert.Equal(4, entries.Count);
        Assert.Equal("credential:TiaMcp/MCP_Safety_01/PLC_F1/Safety", PasswordMapFile.Lookup(entries, "mcp_safety_01", "plc_f1", "safety"));
        Assert.Equal("F1_SAFETY_PW", PasswordMapFile.Lookup(entries, "Other", "PLC_F1", "safety"));
        Assert.Equal("ANY_SAFETY_PW", PasswordMapFile.Lookup(entries, null, "PLC_9", "safety"));
        Assert.Null(PasswordMapFile.Lookup(entries, null, "PLC_9", "ModuleWriteAccessPassword"));
    }

    [Theory]
    [InlineData("{ \"version\": 1, \"entries\": [ { \"plc\": \"PLC_1\", \"purpose\": \"safety\", \"password\": \"Hunter2!\" } ] }", "looks like a secret value field")]
    [InlineData("{ \"version\": 1, \"entries\": [ { \"plc\": \"PLC_1\", \"purpose\": \"safety\", \"env\": \"X\", \"secretValue\": \"Hunter2!\" } ] }", "looks like a secret value field")]
    [InlineData("{ \"version\": 1, \"passwords\": { \"PLC_1\": \"Hunter2!\" }, \"entries\": [] }", "looks like a secret value field")]
    [InlineData("{ \"version\": 1, \"entries\": [ { \"plc\": \"PLC_1\", \"purpose\": \"safety\", \"comment\": \"Hunter2!\", \"env\": \"X\" } ] }", "unknown member")]
    [InlineData("{ \"version\": 1, \"entries\": [ { \"plc\": \"PLC_1\", \"purpose\": \"safety\", \"env\": \"Hunter2!\" } ] }", "environment variable NAME")]
    [InlineData("{ \"version\": 1, \"entries\": [ { \"plc\": \"PLC_1\", \"purpose\": \"safety\", \"credential\": \"Hunter2!\" } ] }", "must start with 'TiaMcp/'")]
    [InlineData("{ \"version\": 1, \"entries\": [ { \"plc\": \"PLC_1\", \"purpose\": \"safety\", \"env\": \"A\", \"credential\": \"TiaMcp/x\" } ] }", "exactly one of env or credential")]
    [InlineData("{ \"version\": 1, \"entries\": [ { \"purpose\": \"TlsVerificationConfiguration\", \"env\": \"A\" } ] }", "not TlsVerificationConfiguration")]
    [InlineData("{ \"version\": 1, \"entries\": [ { \"purpose\": \"safety\", \"env\": \"A\" }, { \"purpose\": \"safety\", \"env\": \"B\" } ] }", "repeats")]
    [InlineData("{ \"version\": 2, \"entries\": [] }", "version")]
    [InlineData("[\"Hunter2!\"]", "JSON object")]
    [InlineData("not json Hunter2!", "not valid JSON")]
    public void MapFile_RefusesValuesUnknownMembersAndBadNames_WithoutEchoingThem(string json, string expected)
    {
        var refused = Assert.Throws<PasswordMapException>(() => PasswordMapFile.Parse(json));
        Assert.Contains(expected, refused.Message);
        Assert.DoesNotContain("Hunter2!", refused.Message);
    }

    [Fact]
    public void MapStore_LoadsFromTheConfiguredPathOrTheAppDataDefault_AndMergesBelowTheCaller()
    {
        var directory = Directory.CreateTempSubdirectory("tia-mcp-password-map-");
        try
        {
            var appData = directory.FullName;
            Assert.Null(PasswordMapStore.ResolvePath(_ => null, appData, File.Exists));
            var defaultPath = Path.Combine(appData, PasswordMapFile.DirectoryName, PasswordMapFile.FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(defaultPath)!);
            File.WriteAllText(defaultPath, ValidMap);
            Assert.Equal(defaultPath, PasswordMapStore.ResolvePath(_ => null, appData, File.Exists));
            Assert.Equal("C:/elsewhere.json", PasswordMapStore.ResolvePath(name => name == PasswordMapFile.EnvironmentVariable ? "C:/elsewhere.json" : null, appData, File.Exists));

            Assert.Null(PasswordMapStore.Load(_ => null, appData));
            Assert.Equal(defaultPath, PasswordMapStore.LoadedPath);
            Assert.Equal("MCP_Safety_01", PasswordMapStore.ProjectName(@"C:\Projects\MCP_Safety_01\MCP_Safety_01.ap21"));

            var merged = PasswordMapStore.Merge(new[] { new PlcDownloadPasswordInfo { Type = "Other", Variable = "MINE" } }, Array.Empty<string>(), null, "PLC_1");
            Assert.Equal(new[] { ("ModuleWriteAccessPassword", "PLC1_WRITE_PW"), ("Other", "MINE") }, merged.Select(m => (m.Type, m.Variable)));
            var callerWins = PasswordMapStore.Merge(new[] { new PlcDownloadPasswordInfo { Type = "ModuleWriteAccessPassword", Variable = "MINE" } }, Array.Empty<string>(), null, "PLC_1");
            Assert.Equal("MINE", Assert.Single(callerWins).Variable);
            Assert.Empty(PasswordMapStore.Merge(null, new[] { "ModuleWriteAccessPassword" }, null, "PLC_1"));

            var safety = TiaMcpServer.Plc.PlcWriteTools.WithMappedSafetyPasswords(new[]
            {
                new TiaMcpServer.Plc.PlcOperationRequest { OperationId = "a", Operation = "safety_login", PlcName = "PLC_F1" },
                new TiaMcpServer.Plc.PlcOperationRequest { OperationId = "b", Operation = "safety_login", PlcName = "PLC_F1", PasswordVariable = "MINE" },
                new TiaMcpServer.Plc.PlcOperationRequest { OperationId = "c", Operation = "safety_logoff", PlcName = "PLC_F1" },
            }, @"C:\P\MCP_Safety_01.ap21");
            Assert.Equal(new[] { "credential:TiaMcp/MCP_Safety_01/PLC_F1/Safety", "MINE", null }, safety.Select(s => s.PasswordVariable));

            // CPU access level purposes belong to set_plc_access_level; downloads, online and upload never receive them.
            File.WriteAllText(defaultPath, "{ \"version\": 1, \"entries\": [ { \"purpose\": \"FullAccess\", \"env\": \"FULL_PW\" }, { \"purpose\": \"PlcMasterSecretPassword\", \"env\": \"SECRET_PW\" } ] }");
            Assert.Null(PasswordMapStore.Load(_ => null, appData));
            Assert.Equal(new[] { "PlcMasterSecretPassword" }, PasswordMapStore.Merge(null, Array.Empty<string>(), null, "PLC_1").Select(m => m.Type));
            var level = new TiaMcpServer.Plc.PlcOperationRequest { OperationId = "l", Operation = "set_plc_access_level", PlcName = "PLC_1", AccessLevel = "NoAccess" };
            TiaMcpServer.Plc.PlcWriteTools.WithMappedSafetyPasswords(new[] { level }, null);
            Assert.Equal(("FullAccess", "FULL_PW"), (level.AccessLevelPasswords!.Entries.Single().Type, level.AccessLevelPasswords.Entries.Single().Variable));

            File.WriteAllText(defaultPath, "{ \"version\": 1, \"entries\": [ { \"purpose\": \"safety\", \"pass\": \"Hunter2!\" } ] }");
            var refused = PasswordMapStore.Load(_ => null, appData);
            Assert.Contains("looks like a secret value field", refused);
            Assert.DoesNotContain("Hunter2!", refused);
        }
        finally
        {
            PasswordMapStore.Set(Array.Empty<PasswordMapEntry>(), null);
            directory.Delete(recursive: true);
        }
    }
}
