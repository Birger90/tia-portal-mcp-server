using System.Diagnostics;
using TiaMcpServer.Cli;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Shared.PasswordManagement;
using TiaMcpServer.PlcDownload;
using Xunit;

namespace TiaMcpServer.Tests.PlcDownload;

/// <summary>
/// The non-UI logic of the "TiaMcp Passwortverwaltung" password manager (Contracts Shared/PasswordManagement): wildcard
/// scopes and precedence of the names-only map, the editor that writes the secret to a credential store and only names to
/// the map, the existence test, the start options and the <c>tia-mcp passwords</c> launcher. The Windows credential store
/// and the WinForms UI are Windows-only and not built here; a recording fake stands in for the store.
/// </summary>
[Collection("Mcp protocol serial")]
public sealed class PasswordManagerTests
{
    private const string Secret = "Hunter2!Geheim";

    private sealed class FakeStore : ICredentialStore
    {
        public Dictionary<string, string> Credentials { get; } = new(StringComparer.Ordinal);
        public List<string> Calls { get; } = new();
        public string? FailWrite { get; set; }

        public CredentialCheckResult Check(string target)
        {
            Calls.Add("check " + target);
            return Credentials.ContainsKey(target)
                ? new CredentialCheckResult(CredentialState.Present, "vorhanden")
                : new CredentialCheckResult(CredentialState.Missing, "fehlt");
        }

        public string? Write(string target, char[] secret, string comment)
        {
            Calls.Add("write " + target);
            if (FailWrite is not null) return FailWrite;
            Credentials[target] = new string(secret);
            return null;
        }

        public string? Delete(string target)
        {
            Calls.Add("delete " + target);
            Credentials.Remove(target);
            return null;
        }
    }

    // ------------------------------------------------------------------ wildcard scopes and precedence

    private const string ScopedMap = """
        { "version": 1, "entries": [
          { "project": "*", "plc": "*", "purpose": "FullAccess", "env": "ALL_PW" },
          { "project": "*", "plc": "PLC_1", "purpose": "FullAccess", "env": "PLC1_ANY_PROJECT_PW" },
          { "project": "Anlage_01", "plc": "*", "purpose": "FullAccess", "env": "ANLAGE_PW" },
          { "project": "Anlage_01", "plc": "PLC_1", "purpose": "FullAccess", "env": "ANLAGE_PLC1_PW" } ] }
        """;

    [Theory]
    [InlineData("Anlage_01", "PLC_1", "ANLAGE_PLC1_PW")]
    [InlineData("anlage_01", "plc_1", "ANLAGE_PLC1_PW")]
    [InlineData("Anlage_01", "PLC_2", "ANLAGE_PW")]
    [InlineData("Anderes", "PLC_1", "PLC1_ANY_PROJECT_PW")]
    [InlineData("Anderes", "PLC_2", "ALL_PW")]
    [InlineData(null, "PLC_2", "ALL_PW")]
    public void Map_WildcardScopes_MostSpecificWins_ProjectAndPlcThenProjectThenPlcThenAll(string? project, string plc, string expected)
        => Assert.Equal(expected, PasswordMapFile.Lookup(PasswordMapFile.Parse(ScopedMap), project, plc, "FullAccess"));

    [Fact]
    public void Map_ProjectOnlyOutranksPlcOnly()
    {
        var entries = PasswordMapFile.Parse("""
            { "version": 1, "entries": [
              { "plc": "PLC_1", "purpose": "safety", "env": "PLC1_PW" },
              { "project": "Anlage_01", "purpose": "safety", "env": "ANLAGE_PW" } ] }
            """);
        Assert.Equal("ANLAGE_PW", PasswordMapFile.Lookup(entries, "Anlage_01", "PLC_1", "safety"));
        Assert.Equal(2, PasswordMapFile.Specificity(entries[1]));
        Assert.Equal(1, PasswordMapFile.Specificity(entries[0]));
    }

    [Fact]
    public void Map_StarAndOmittedAreTheSameScope()
    {
        var entries = PasswordMapFile.Parse("""{ "version": 1, "entries": [ { "project": "*", "plc": "*", "purpose": "safety", "env": "A" } ] }""");
        Assert.Null(entries[0].Project);
        Assert.Null(entries[0].Plc);
        var repeated = Assert.Throws<PasswordMapException>(() => PasswordMapFile.Parse(
            """{ "version": 1, "entries": [ { "project": "*", "purpose": "safety", "env": "A" }, { "purpose": "safety", "env": "B" } ] }"""));
        Assert.Contains("repeats", repeated.Message);
    }

    [Fact]
    public void MapStore_WildcardEntriesAnswerDownloadPasswords()
    {
        PasswordMapStore.Set(PasswordMapFile.Parse("""
            { "version": 1, "entries": [
              { "project": "*", "plc": "*", "purpose": "ModuleWriteAccessPassword", "credential": "TiaMcp/Alle Projekte/Alle PLCs/ModuleWriteAccessPassword" },
              { "project": "Anlage_01", "plc": "*", "purpose": "ModuleWriteAccessPassword", "env": "ANLAGE_WRITE_PW" } ] }
            """), null);
        try
        {
            Assert.Equal(new[] { ("ModuleWriteAccessPassword", "ANLAGE_WRITE_PW") }, PasswordMapStore.Missing(@"C:\P\Anlage_01.ap21", "PLC_1", Array.Empty<string>()).ToArray());
            Assert.Equal(new[] { ("ModuleWriteAccessPassword", "credential:TiaMcp/Alle Projekte/Alle PLCs/ModuleWriteAccessPassword") },
                PasswordMapStore.Missing(@"C:\P\Other.ap21", "PLC_1", Array.Empty<string>()).ToArray());
        }
        finally
        {
            PasswordMapStore.Set(Array.Empty<PasswordMapEntry>(), null);
        }
    }

    [Fact]
    public void Serialize_WritesStarsAndNamesOnly_AndRoundTrips()
    {
        var entries = PasswordMapFile.Parse(ScopedMap).Append(new PasswordMapEntry("Anlage_01", null, "safety", "credential:TiaMcp/Anlage_01/Alle PLCs/Safety")).ToList();
        var json = PasswordMapFile.Serialize(entries);
        Assert.Contains("\"project\": \"*\"", json);
        Assert.Contains("\"plc\": \"*\"", json);
        Assert.Contains("\"credential\": \"TiaMcp/Anlage_01/Alle PLCs/Safety\"", json);
        var again = PasswordMapFile.Parse(json);
        Assert.Equal(entries.Select(e => (e.Project, e.Plc, e.Purpose, e.Source)), again.Select(e => (e.Project, e.Plc, e.Purpose, e.Source)));
    }

    [Fact]
    public void Serialize_RefusesAnEntryTheServerWouldRefuse()
        => Assert.Throws<PasswordMapException>(() => PasswordMapFile.Serialize(new[] { new PasswordMapEntry(null, null, "safety", "not a name!") }));

    // ------------------------------------------------------------------ editor

    [Fact]
    public void Save_WritesTheSecretToTheStore_AndOnlyTheTargetToTheMap()
    {
        var store = new FakeStore();
        var editor = new PasswordMapEditor(null, null);
        var result = editor.Save(null, null, "FullAccess", Secret.ToCharArray(), store);

        Assert.True(result.Saved, result.Error);
        Assert.Equal("TiaMcp/Alle Projekte/Alle PLCs/FullAccess", Assert.Single(store.Credentials.Keys));
        Assert.Equal(Secret, store.Credentials["TiaMcp/Alle Projekte/Alle PLCs/FullAccess"]);
        var entry = Assert.Single(editor.Entries);
        Assert.Equal("credential:TiaMcp/Alle Projekte/Alle PLCs/FullAccess", entry.Source);
        var json = editor.ToJson();
        Assert.DoesNotContain(Secret, json);
        Assert.Contains("\"project\": \"*\"", json);
    }

    [Theory]
    [InlineData("Anlage_01", "PLC_F1", "safety", "TiaMcp/Anlage_01/PLC_F1/Safety")]
    [InlineData("Anlage_01", null, "PlcMasterSecretPassword", "TiaMcp/Anlage_01/Alle PLCs/PlcMasterSecretPassword")]
    [InlineData(null, "PLC_1", "ModuleWriteAccessPassword", "TiaMcp/Alle Projekte/PLC_1/ModuleWriteAccessPassword")]
    [InlineData("A/B", "C\\D", "HMIAccess", "TiaMcp/A_B/C_D/HMIAccess")]
    public void TargetFor_NamesProjectPlcAndPurpose(string? project, string? plc, string purpose, string expected)
        => Assert.Equal(expected, new PasswordMapEditor(null, null).TargetFor(project, plc, purpose));

    [Fact]
    public void TargetFor_UsesTheOwnerPrefix_AndTheDefaultUnderStar()
    {
        Assert.Equal("Anlage/Linie1/Alle Projekte/PLC_1/FullAccess", new PasswordMapEditor(null, "Anlage/Linie1/").TargetFor(null, "PLC_1", "FullAccess"));
        var any = new PasswordMapEditor(null, "*");
        Assert.Equal("TiaMcp/Alle Projekte/PLC_1/FullAccess", any.TargetFor(null, "PLC_1", "FullAccess"));
        Assert.Equal("*", any.AcceptedPrefix);
    }

    [Fact]
    public void Save_RefusesADuplicateScopeAndPurpose_WithoutTouchingTheStore()
    {
        var store = new FakeStore();
        var editor = new PasswordMapEditor(null, null);
        Assert.True(editor.Save("Anlage_01", null, "safety", Secret.ToCharArray(), store).Saved);
        store.Calls.Clear();
        var again = editor.Save("ANLAGE_01", null, "safety", "other".ToCharArray(), store);
        Assert.False(again.Saved);
        Assert.Contains("schon einen Eintrag", again.Error);
        Assert.Empty(store.Calls);
        Assert.Single(editor.Entries);
    }

    [Fact]
    public void Save_LeavesTheMapUnchangedWhenTheStoreRefuses()
    {
        var store = new FakeStore { FailWrite = "Die Anmeldeinformation konnte nicht gespeichert werden: Zugriff verweigert." };
        var editor = new PasswordMapEditor(null, null);
        var result = editor.Save(null, null, "safety", Secret.ToCharArray(), store);
        Assert.False(result.Saved);
        Assert.Equal(store.FailWrite, result.Error);
        Assert.Empty(editor.Entries);
    }

    [Fact]
    public void Save_RefusesATargetCollision()
    {
        var store = new FakeStore();
        var editor = new PasswordMapEditor(null, null);
        Assert.True(editor.Save(null, "PLC_1", "FullAccess", Secret.ToCharArray(), store).Saved);
        // A project literally named like the wildcard segment would get the same target.
        var clash = editor.Save(PasswordMapEditor.AllProjectsSegment, "PLC_1", "FullAccess", Secret.ToCharArray(), store);
        Assert.False(clash.Saved);
        Assert.Contains("schon von einem anderen Eintrag", clash.Error);
    }

    [Fact]
    public void Save_ChangingThePasswordKeepsAHandWrittenTarget()
    {
        var existing = new PasswordMapEntry("Anlage_01", "PLC_F1", "safety", "credential:TiaMcp/handmade/F1");
        var editor = new PasswordMapEditor(new[] { existing }, null);
        var store = new FakeStore();
        var result = editor.Save("Anlage_01", "PLC_F1", "safety", "neu".ToCharArray(), store, existing);
        Assert.True(result.Saved, result.Error);
        Assert.Equal("credential:TiaMcp/handmade/F1", Assert.Single(editor.Entries).Source);
        Assert.Equal(new[] { "write TiaMcp/handmade/F1" }, store.Calls);
        Assert.Null(result.ObsoleteTarget);
    }

    [Fact]
    public void Save_ChangingTheScopeWritesANewTarget_AndReportsTheOldOneAsObsolete()
    {
        var store = new FakeStore();
        var editor = new PasswordMapEditor(null, null);
        var first = editor.Save("Anlage_01", null, "safety", Secret.ToCharArray(), store).Entry!;
        var moved = editor.Save(null, null, "safety", Secret.ToCharArray(), store, first);
        Assert.True(moved.Saved, moved.Error);
        Assert.Equal("TiaMcp/Anlage_01/Alle PLCs/Safety", moved.ObsoleteTarget);
        Assert.Equal("credential:TiaMcp/Alle Projekte/Alle PLCs/Safety", Assert.Single(editor.Entries).Source);
    }

    [Fact]
    public void Save_ReplacesAnEnvironmentEntryWithACredential()
    {
        var existing = new PasswordMapEntry(null, "PLC_1", "ModuleWriteAccessPassword", "PLC1_WRITE_PW");
        var editor = new PasswordMapEditor(new[] { existing }, null);
        var result = editor.Save(null, "PLC_1", "ModuleWriteAccessPassword", Secret.ToCharArray(), new FakeStore(), existing);
        Assert.True(result.Saved, result.Error);
        Assert.Equal("credential:TiaMcp/Alle Projekte/PLC_1/ModuleWriteAccessPassword", Assert.Single(editor.Entries).Source);
        Assert.Null(result.ObsoleteTarget);
    }

    [Fact]
    public void Save_ReportsOtherEntriesSharingTheTarget()
    {
        var a = new PasswordMapEntry(null, "PLC_1", "FullAccess", "credential:TiaMcp/shared");
        var b = new PasswordMapEntry(null, "PLC_1", "ModuleWriteAccessPassword", "credential:TiaMcp/shared");
        var editor = new PasswordMapEditor(new[] { a, b }, null);
        var result = editor.Save(null, "PLC_1", "FullAccess", Secret.ToCharArray(), new FakeStore(), a);
        Assert.True(result.Saved, result.Error);
        Assert.Equal(1, result.SharedWith);
    }

    [Fact]
    public void Remove_ReturnsTheTargetOnlyWhenNoOtherEntryUsesIt()
    {
        var a = new PasswordMapEntry(null, "PLC_1", "FullAccess", "credential:TiaMcp/shared");
        var b = new PasswordMapEntry(null, "PLC_1", "ModuleWriteAccessPassword", "credential:TiaMcp/shared");
        var c = new PasswordMapEntry(null, null, "safety", "SAFETY_PW");
        var editor = new PasswordMapEditor(new[] { a, b, c }, null);
        Assert.Null(editor.Remove(a));
        Assert.Equal("TiaMcp/shared", editor.Remove(b));
        Assert.Null(editor.Remove(c));
        Assert.Empty(editor.Entries);
    }

    [Theory]
    [InlineData("", "Projekt", "Namen eingeben")]
    [InlineData(" Anlage", "Projekt", "Leerzeichen")]
    [InlineData("Anl*", "PLC", "'*'")]
    [InlineData("A\tB", "PLC", "Steuerzeichen")]
    public void ScopeError_RefusesBadNames(string name, string what, string expected)
        => Assert.Contains(expected, PasswordMapEditor.ScopeError(name, what));

    [Fact]
    public void ScopeError_NullMeansEvery() => Assert.Null(PasswordMapEditor.ScopeError(null, "Projekt"));

    [Fact]
    public void SecretError_ChecksPresenceRepetitionAndLength_WithoutEchoingTheSecret()
    {
        Assert.Null(PasswordMapEditor.SecretError(Secret.ToCharArray(), Secret.ToCharArray()));
        foreach (var error in new[]
                 {
                     PasswordMapEditor.SecretError(Array.Empty<char>(), Array.Empty<char>()),
                     PasswordMapEditor.SecretError(Secret.ToCharArray(), (Secret + "x").ToCharArray()),
                     PasswordMapEditor.SecretError(Secret.ToCharArray(), Secret.ToUpperInvariant().ToCharArray()),
                     PasswordMapEditor.SecretError(new string('a', PasswordMapEditor.MaxSecretLength + 1).ToCharArray(), new string('a', PasswordMapEditor.MaxSecretLength + 1).ToCharArray()),
                 })
        {
            Assert.NotNull(error);
            Assert.DoesNotContain(Secret, error);
        }
    }

    [Theory]
    [InlineData("TlsVerificationConfiguration")]
    [InlineData("1abc")]
    [InlineData("Module Write")]
    [InlineData("")]
    public void Save_RefusesPurposesTheMapRefuses(string purpose)
    {
        var store = new FakeStore();
        Assert.False(new PasswordMapEditor(null, null).Save(null, null, purpose, Secret.ToCharArray(), store).Saved);
        Assert.Empty(store.Calls);
    }

    [Fact]
    public void Purposes_CoverTheMapPurposesOfTheServer()
    {
        var purposes = PasswordPurposes.Known.Select(p => p.Purpose).ToList();
        Assert.Contains(PasswordMapFile.SafetyPurpose, purposes);
        Assert.Contains(TiaMcpServer.Contracts.Plc.Security.PlcSecurityPolicy.MasterSecretPurpose, purposes);
        foreach (var level in TiaMcpServer.Contracts.Plc.Security.PlcSecurityPolicy.PasswordLevels) Assert.Contains(level, purposes);
        Assert.All(purposes, p => Assert.Null(PasswordPurposes.Error(p)));
        Assert.Equal("Zugriffsstufe: Vollzugriff", PasswordPurposes.LabelOf("FullAccess"));
        Assert.Equal("Passwortkonfiguration BlockBindingPassword", PasswordPurposes.LabelOf("BlockBindingPassword"));
    }

    // ------------------------------------------------------------------ test button

    [Fact]
    public void Test_ChecksExistenceOnly()
    {
        var store = new FakeStore();
        var editor = new PasswordMapEditor(null, null);
        var entry = editor.Save(null, null, "safety", Secret.ToCharArray(), store).Entry!;
        var present = editor.Test(entry, store, _ => null);
        Assert.Equal(CredentialState.Present, present.State);
        Assert.True(present.Usable);
        Assert.DoesNotContain(Secret, present.Message);

        store.Credentials.Clear();
        Assert.Equal(CredentialState.Missing, editor.Test(entry, store, _ => null).State);
    }

    [Fact]
    public void Test_ReportsATargetOutsideTheServerPrefix_WithoutAskingTheStore()
    {
        var store = new FakeStore();
        var editor = new PasswordMapEditor(new[] { new PasswordMapEntry(null, null, "safety", "credential:TiaMcp/x") }, "Anlage/");
        var result = editor.Test(editor.Entries[0], store, _ => null);
        Assert.Equal(CredentialState.PrefixMismatch, result.State);
        Assert.Contains("Anlage/", result.Message);
        Assert.Empty(store.Calls);
    }

    [Fact]
    public void Test_EnvironmentEntries_ReportWhetherTheVariableIsSet()
    {
        var editor = new PasswordMapEditor(new[] { new PasswordMapEntry(null, "PLC_1", "ModuleWriteAccessPassword", "PLC1_WRITE_PW") }, null);
        Assert.Equal(CredentialState.EnvironmentSet, editor.Test(editor.Entries[0], new FakeStore(), name => name == "PLC1_WRITE_PW" ? "x" : null).State);
        var unset = editor.Test(editor.Entries[0], new FakeStore(), _ => null);
        Assert.Equal(CredentialState.EnvironmentUnset, unset.State);
        Assert.Contains("MCP-Client", unset.Message);
    }

    // ------------------------------------------------------------------ file

    [Fact]
    public void WriteFile_CreatesTheFolder_ReplacesTheFile_AndLeavesNoTemporaryFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tiamcp-pwm-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "TiaMcpServer", "passwords.json");
        try
        {
            var store = new FakeStore();
            var editor = new PasswordMapEditor(null, null);
            editor.Save(null, null, "safety", Secret.ToCharArray(), store);
            editor.WriteFile(path);
            editor.Save("Anlage_01", "PLC_1", "FullAccess", Secret.ToCharArray(), store);
            editor.WriteFile(path);

            var text = File.ReadAllText(path);
            Assert.DoesNotContain(Secret, text);
            Assert.Equal(2, PasswordMapFile.Parse(text).Count);
            Assert.Equal(new[] { path }, Directory.GetFiles(Path.GetDirectoryName(path)!));
            Assert.Equal(2, PasswordMapEditor.FromJson(text, null).Entries.Count);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WriteFile_RefusesDevicePaths()
        => Assert.Throws<IOException>(() => new PasswordMapEditor(null, null).WriteFile(@"\\?\C:\x\passwords.json"));

    [Fact]
    public void FromJson_RefusesAnInvalidMap_SoTheManagerNeverOverwritesIt()
    {
        Assert.Throws<PasswordMapException>(() => PasswordMapEditor.FromJson("""{ "version": 1, "entries": [ { "purpose": "safety", "password": "x" } ] }""", null));
        Assert.Empty(PasswordMapEditor.FromJson("  ", null).Entries);
    }

    // ------------------------------------------------------------------ options and launcher

    [Fact]
    public void Options_PrefixFromArgumentThenEnvironmentThenDefault_MapFromArgumentThenEnvironmentThenAppData()
    {
        var env = new Dictionary<string, string?> { [PasswordSources.CredentialPrefixVariable] = "Env/", [PasswordMapFile.EnvironmentVariable] = @"\\server\share\passwords.json" };
        var fromArgs = PasswordManagerOptions.Parse(new[] { "--prefix", "Arg/", "--map=C:\\m.json" }, n => env.GetValueOrDefault(n), "/appdata");
        Assert.Null(fromArgs.Error);
        Assert.Equal("Arg/", fromArgs.Prefix);
        Assert.Equal("C:\\m.json", fromArgs.MapPath);

        var fromEnv = PasswordManagerOptions.Parse(Array.Empty<string>(), n => env.GetValueOrDefault(n), "/appdata");
        Assert.Equal("Env/", fromEnv.Prefix);
        Assert.Contains(PasswordSources.CredentialPrefixVariable, fromEnv.PrefixSource);
        Assert.Equal(@"\\server\share\passwords.json", fromEnv.MapPath);

        var defaults = PasswordManagerOptions.Parse(Array.Empty<string>(), _ => null, "/appdata");
        Assert.Equal(PasswordSources.CredentialTargetPrefix, defaults.Prefix);
        Assert.Equal(Path.Combine("/appdata", "TiaMcpServer", "passwords.json"), defaults.MapPath);
    }

    [Theory]
    [InlineData("--password", "Unbekanntes Argument")]
    [InlineData("--prefix=Bad*Prefix", "'*'")]
    [InlineData("--prefix", "braucht einen Wert")]
    [InlineData("--map=", "braucht einen Pfad")]
    public void Options_RefuseUnknownArgumentsAndBadValues(string argument, string expected)
        => Assert.Contains(expected, PasswordManagerOptions.Parse(new[] { argument }, _ => null, "/appdata").Error);

    [Fact]
    public void Launcher_StartsThePasswordManagerNextToTheServer_WithTheArguments()
    {
        ProcessStartInfo? started = null;
        var output = new StringWriter();
        var exit = PasswordManagerCommand.Run(new[] { "--prefix", "Anlage/" }, output, new StringWriter(), "/tool", isWindows: true, _ => true, info => { started = info; return true; });
        Assert.Equal(0, exit);
        Assert.Equal(Path.Combine("/tool", "password-manager", "TiaMcpPasswortverwaltung.exe"), started!.FileName);
        Assert.Equal(new[] { "--prefix", "Anlage/" }, started.ArgumentList);
        Assert.False(started.UseShellExecute);
    }

    [Fact]
    public void Launcher_RefusesOffWindowsAndWhenNotInstalled()
    {
        var error = new StringWriter();
        Assert.Equal(1, PasswordManagerCommand.Run(Array.Empty<string>(), new StringWriter(), error, "/tool", isWindows: false, _ => true, _ => true));
        Assert.Contains("Windows only", error.ToString());

        error = new StringWriter();
        Assert.Equal(1, PasswordManagerCommand.Run(Array.Empty<string>(), new StringWriter(), error, "/tool", isWindows: true, _ => false, _ => true));
        Assert.Contains("not installed", error.ToString());

        var help = new StringWriter();
        Assert.Equal(0, PasswordManagerCommand.Run(new[] { "--help" }, help, new StringWriter(), "/tool", isWindows: false, _ => false, _ => false));
        Assert.Contains("tia-mcp passwords", help.ToString());
    }
}
