using System.Text.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Safety;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Plc;
using TiaMcpServer.PlcDownload;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Plc.Safety;

/// <summary>The Siemens-free fail-safe rules, the runtime binding on plain .NET stand-ins and the plc_write/plc_download policies; no worker runs.</summary>
[Collection("Mcp protocol serial")]
public sealed class PlcSafetyPolicyTests
{
    // ------------------------------------------------------------------ edit permission

    [Theory]
    [InlineData(true, true, true, false, PlcSafetyEditPermission.Permitted)]
    [InlineData(true, true, false, false, PlcSafetyEditPermission.LoginRequired)]
    [InlineData(true, true, false, true, PlcSafetyEditPermission.PendingEarlierLogin)]
    [InlineData(true, false, false, true, PlcSafetyEditPermission.NoPasswordSet)]
    [InlineData(true, false, false, false, PlcSafetyEditPermission.NoPasswordSet)]
    [InlineData(true, false, true, false, PlcSafetyEditPermission.NoPasswordSet)]
    [InlineData(true, null, true, false, PlcSafetyEditPermission.Unverifiable)]
    [InlineData(true, true, null, false, PlcSafetyEditPermission.Unverifiable)]
    [InlineData(false, true, true, false, PlcSafetyEditPermission.NotAnFCpu)]
    public void Decide_RefusesOnlyAPasswordProtectedProgramWithoutLogin(bool available, bool? passwordSet, bool? loggedOn, bool earlierLogin, PlcSafetyEditPermission expected)
    {
        var decision = PlcSafetyPolicy.Decide(available, passwordSet, loggedOn, earlierLogin);
        Assert.Equal(expected, decision);
        Assert.Equal(decision != PlcSafetyEditPermission.LoginRequired, PlcSafetyPolicy.MayPlan(decision));
        Assert.Equal(PlcSafetyPolicy.MayPlan(decision), PlcSafetyPolicy.Refusal(decision, "PLC_1", "x") is null);
        // What this server cannot judge is passed to TIA Portal with a note, never refused.
        Assert.Equal(decision is PlcSafetyEditPermission.NotAnFCpu or PlcSafetyEditPermission.Unverifiable, PlcSafetyPolicy.Undetermined(decision, "PLC_1", "x") is not null);
    }

    [Fact]
    public void Refusals_NameTheNextStep_AndNeverAPassword()
    {
        Assert.Contains("safety_login", PlcSafetyPolicy.Refusal(PlcSafetyEditPermission.LoginRequired, "PLC_1", "x"));
        Assert.Contains("passwordVariable", PlcSafetyPolicy.Refusal(PlcSafetyEditPermission.LoginRequired, "PLC_1", "x"));
        Assert.Null(PlcSafetyPolicy.Refusal(PlcSafetyEditPermission.NoPasswordSet, "PLC_1", "x"));
        Assert.Contains("get_safety_administration", PlcSafetyPolicy.Undetermined(PlcSafetyEditPermission.Unverifiable, "PLC_1", "x"));
        Assert.Contains("TIA Portal", PlcSafetyPolicy.Undetermined(PlcSafetyEditPermission.NotAnFCpu, "PLC_1", "x"));
        Assert.Contains("never replaces the safety acceptance", PlcSafetyPolicy.AcceptanceNotice);
    }

    [Theory]
    [InlineData("<AttributeList><ProgrammingLanguage>F_LAD</ProgrammingLanguage></AttributeList>", true)]
    [InlineData("<ProgrammingLanguage> F_FBD </ProgrammingLanguage>", true)]
    [InlineData("<ProgrammingLanguage>F_DB</ProgrammingLanguage>", true)]
    [InlineData("<ProgrammingLanguage>LAD</ProgrammingLanguage>", false)]
    [InlineData("<ProgrammingLanguage>SCL</ProgrammingLanguage><!-- F_LAD -->", false)]
    [InlineData("FUNCTION_BLOCK \"F_Main\"", false)]
    [InlineData(null, false)]
    public void DeclaresFailSafeLanguage_ReadsOnlyTheProgrammingLanguageElement(string? document, bool expected)
        => Assert.Equal(expected, PlcSafetyPolicy.DeclaresFailSafeLanguage(document));

    [Fact]
    public void FailSafeLanguage_IsTheOrdinalFPrefix()
    {
        Assert.True(PlcSafetyPolicy.IsFailSafeLanguage("F_LAD"));
        Assert.False(PlcSafetyPolicy.IsFailSafeLanguage("f_lad"));
        Assert.False(PlcSafetyPolicy.IsFailSafeLanguage("FBD"));
        Assert.False(PlcSafetyPolicy.IsFailSafeLanguage(null));
    }

    [Fact]
    public void SignatureText_GivesDecimalAndHexOfIntegralValues()
    {
        Assert.Equal(("3735928559", "0xDEADBEEF"), PlcSafetyPolicy.SignatureText(0xDEADBEEFu));
        Assert.Equal(("-1", "0xFFFFFFFF"), PlcSafetyPolicy.SignatureText(-1));
        Assert.Equal(("0", "0x0"), PlcSafetyPolicy.SignatureText(0UL));
        Assert.Equal(("abc", null), PlcSafetyPolicy.SignatureText("abc"));
        Assert.Equal(((string?)null, (string?)null), PlcSafetyPolicy.SignatureText(null));
    }

    [Theory]
    [InlineData(false, false, "Create(String)")]
    [InlineData(true, false, "Create(String, PlcBlock)")]
    [InlineData(true, true, "Create(String, PlcBlock, PlcBlock)")]
    public void RuntimeGroupOverload_FollowsTheGivenBlocks(bool main, bool instance, string expected)
        => Assert.Equal(expected, PlcSafetyPolicy.RuntimeGroupOverload(main, instance));

    // ------------------------------------------------------------------ runtime binding on stand-ins

    public enum FakeSignatureType { Collective, Block }

    public sealed class FakeSignature
    {
        public FakeSignatureType Type { get; set; }
        public uint Value { get; set; }
    }

    public sealed class FakeProvider
    {
        public List<FakeSignature> Signatures { get; } = new() { new() { Type = FakeSignatureType.Collective, Value = 0x1234 } };
    }

    public sealed class FakeVersion
    {
        public override string ToString() => "V2.6";
    }

    public sealed class FakeAdministration
    {
        public bool IsSafetyOfflineProgramPasswordSet { get; set; } = true;
        public bool IsLoggedOnToSafetyOfflineProgram { get; set; }
        public FakeVersion SafetySystemVersion { get; } = new();
        public string Throwing => throw new InvalidOperationException("unreadable on purpose");
        public string Fatal => throw new OutOfMemoryException("fatal on purpose");
        public System.Security.SecureString? Received { get; private set; }
        public void LoginToSafetyOfflineProgram(System.Security.SecureString password)
        {
            Received = password;
            IsLoggedOnToSafetyOfflineProgram = true;
        }
        public bool SafetyModificationsPossible() => true;
        public void SafetyModificationsPossible(bool value) => throw new InvalidOperationException("setters are never called");
    }

    private static bool NeverFatal(Exception exception) => exception is OutOfMemoryException;

    [Fact]
    public void Read_ReportsValuesMissingMembersAndUnreadableMembers()
    {
        var admin = new FakeAdministration();
        var set = PlcSafetyReflection.Read(admin, PlcSafetyApiCatalog.IsPasswordSet, NeverFatal);
        Assert.Equal((PlcSafetyValueStates.Read, "true", "System.Boolean"), (set.State, set.Value, set.ValueType));
        Assert.Equal("V2.6", PlcSafetyReflection.Read(admin, "SafetySystemVersion", NeverFatal).Value);
        var missing = PlcSafetyReflection.Read(admin, "ProgramSignatures", NeverFatal);
        Assert.Equal(PlcSafetyValueStates.ApiMemberMissing, missing.State);
        Assert.StartsWith("api_member_missing", missing.Error);
        var unreadable = PlcSafetyReflection.Read(admin, "Throwing", NeverFatal);
        Assert.Equal((PlcSafetyValueStates.Unreadable, "unreadable on purpose"), (unreadable.State, unreadable.Error));
        Assert.Throws<OutOfMemoryException>(() => PlcSafetyReflection.Read(admin, "Fatal", NeverFatal));
        // A missing property falls back to the attribute reader when one is given.
        Assert.Equal("RTG1", PlcSafetyReflection.Read(admin, "MainSafetyBlockName", NeverFatal, _ => "RTG1").Value);
    }

    [Fact]
    public void CallGetter_CallsOnlyTheParameterlessOverload()
    {
        var getter = PlcSafetyReflection.CallGetter(new FakeAdministration(), "SafetyModificationsPossible", NeverFatal);
        Assert.Equal((PlcSafetyValueStates.Read, "true"), (getter.State, getter.Value));
        Assert.Equal(PlcSafetyValueStates.ApiMemberMissing, PlcSafetyReflection.CallGetter(new FakeAdministration(), "UsernameForFChangeHistory", NeverFatal).State);
    }

    [Fact]
    public void Invoke_BindsTheExactSecureStringSignature_OrReportsTheMissingMember()
    {
        var admin = new FakeAdministration();
        using var password = new System.Security.SecureString();
        PlcSafetyReflection.Invoke(admin, PlcSafetyApiCatalog.Login, new[] { typeof(System.Security.SecureString) }, new object?[] { password });
        Assert.Same(password, admin.Received);
        Assert.True(PlcSafetyReflection.GetBool(admin, PlcSafetyApiCatalog.IsLoggedOn));
        var missing = Assert.Throws<PlcSafetyApiMissingException>(() =>
            PlcSafetyReflection.Invoke(admin, PlcSafetyApiCatalog.Logoff, Type.EmptyTypes, Array.Empty<object?>()));
        Assert.Contains("LogoffFromSafetyOfflineProgram", missing.Member);
        Assert.Throws<PlcSafetyApiMissingException>(() => PlcSafetyReflection.GetBool(admin, "SafetySystemVersion"));
    }

    [Fact]
    public void Signatures_ReadTypeAndValueOfEveryEntry()
    {
        var row = Assert.Single(PlcSafetyReflection.Signatures(new FakeProvider()));
        Assert.Equal(("Collective", "4660", "0x1234"), (row.Type, row.Value, row.Hex));
        Assert.Throws<PlcSafetyApiMissingException>(() => PlcSafetyReflection.Signatures(new object()));
    }

    // ------------------------------------------------------------------ API catalog

    [Fact]
    public void ApiCatalog_NamesEvidenceForEveryMember_AndMarksTheSiemensPrimaryOnes()
    {
        Assert.All(PlcSafetyApiCatalog.Members, member =>
        {
            Assert.False(string.IsNullOrWhiteSpace(member.Source));
            Assert.Contains(member.Evidence, new[] { PlcSafetyApiCatalog.SiemensSkill, PlcSafetyApiCatalog.V21Baseline, PlcSafetyApiCatalog.ThirdPartyUse });
        });
        // The access members come only from the installed-API baseline; they are reflection-bound and reported missing when absent.
        foreach (var name in new[] { PlcSafetyApiCatalog.Login, PlcSafetyApiCatalog.Logoff, PlcSafetyApiCatalog.SetPassword, PlcSafetyApiCatalog.RevokePassword, PlcSafetyApiCatalog.IsLoggedOn })
            Assert.False(PlcSafetyApiCatalog.Members.Single(m => m.Name == name).SiemensPrimary);
        Assert.True(PlcSafetyApiCatalog.Members.Single(m => m.Name == PlcSafetyApiCatalog.RuntimeGroups).SiemensPrimary);
    }

    // ------------------------------------------------------------------ catalogs and access modes

    [Fact]
    public void Methods_AreReadsInEveryModeAndWritesOnlyInWritableModes()
    {
        foreach (var read in PlcSafetyMethods.Read)
        {
            Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, read));
            Assert.Equal("plc_read", ProjectCapabilityCatalog.OperationFor(read));
        }

        foreach (var write in PlcSafetyMethods.Write)
        {
            Assert.False(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, write));
            Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadWrite, write));
            Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.Full, write));
            Assert.Equal("plc_write", ProjectCapabilityCatalog.OperationFor(write));
        }

        Assert.Equal(new[] { "get_safety_administration", "list_safety_blocks" }, PlcOperationCatalog.ReadOperationNames.TakeLast(2));
        Assert.Equal(PlcSafetyMethods.Write, PlcOperationCatalog.WriteOperationNames.TakeLast(6));
        Assert.True(WorkerFailureCategories.IsKnown(WorkerFailureCategories.ApiMemberMissing));
    }

    private static PlcOperationRequest Op(string operation, Action<PlcOperationRequest>? configure = null)
    {
        var item = new PlcOperationRequest { OperationId = "s", Operation = operation };
        configure?.Invoke(item);
        return item;
    }

    private static string? WriteError(PlcOperationRequest item)
    {
        var validation = PlcOperationCatalog.ValidateWrite(new[] { item });
        return validation.IsValid ? null : validation.Error;
    }

    [Fact]
    public void Validation_RequiresAPasswordVariableName_AndRefusesRedactedValuesWithoutEchoingThem()
    {
        Assert.Contains("passwordVariable", WriteError(Op(PlcSafetyMethods.Login)));
        Assert.Null(WriteError(Op(PlcSafetyMethods.Login, o => o.PasswordVariable = "PLC_SAFETY_PW")));
        Assert.Null(WriteError(Op(PlcSafetyMethods.Logoff)));
        Assert.Contains("never the password itself", WriteError(Op(PlcSafetyMethods.SetPassword, o => o.PasswordVariable = PlcDownloadPasswordMap.RedactedVariable)));
        Assert.Contains("not set", WriteError(Op(PlcSafetyMethods.RevokePassword, o => o.PasswordVariable = PlcDownloadPasswordMap.UnsetVariable)));
        Assert.Contains("not valid for safety_logoff", WriteError(Op(PlcSafetyMethods.Logoff, o => o.PasswordVariable = "PLC_SAFETY_PW")));
    }

    [Fact]
    public void Validation_ChecksTheRuntimeGroupAndItsBlocks()
    {
        Assert.Contains("runtimeGroupName", WriteError(Op(PlcSafetyMethods.CreateRuntimeGroup)));
        Assert.Null(WriteError(Op(PlcSafetyMethods.CreateRuntimeGroup, o => o.RuntimeGroupName = "RTG2")));
        Assert.Contains("must not contain", WriteError(Op(PlcSafetyMethods.CreateRuntimeGroup, o => o.RuntimeGroupName = "A/B")));
        Assert.Contains("needs mainSafetyBlockPath", WriteError(Op(PlcSafetyMethods.CreateRuntimeGroup, o =>
        {
            o.RuntimeGroupName = "RTG2";
            o.MainSafetyInstanceDbPath = "PLC_1/Main_Safety_DB";
        })));
        Assert.Contains("must start with the PLC name", WriteError(Op(PlcSafetyMethods.CreateRuntimeGroup, o =>
        {
            o.RuntimeGroupName = "RTG2";
            o.MainSafetyBlockPath = "Main_Safety";
        })));
        Assert.Null(WriteError(Op(PlcSafetyMethods.CreateRuntimeGroup, o =>
        {
            o.RuntimeGroupName = "RTG2";
            o.MainSafetyBlockPath = "PLC_1/Main_Safety";
            o.MainSafetyInstanceDbPath = "PLC_1/Main_Safety_DB";
        })));
    }

    [Theory]
    [InlineData(-1, null, "'offset'")]
    [InlineData(null, 0, "'limit'")]
    [InlineData(null, 2001, "'limit'")]
    public void ListBlocks_PagesWithinBounds(int? offset, int? limit, string expected)
    {
        var validation = PlcOperationCatalog.ValidateRead(new[] { Op(PlcSafetyMethods.ListBlocks, o => { o.Offset = offset; o.Limit = limit; }) });
        Assert.Contains(expected, validation.Error);
    }

    [Fact]
    public void PasswordVariable_IsRedactedWhileTheRequestIsRead()
    {
        const string variable = "TIA_MCP_TEST_SAFETY_POLICY_PW";
        Environment.SetEnvironmentVariable(variable, "x");
        try
        {
            static string? Bound(string json) => JsonSerializer.Deserialize<PlcOperationRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.PasswordVariable;
            Assert.Equal(variable, Bound($$"""{"operationId":"a","operation":"safety_login","passwordVariable":"{{variable}}"}"""));
            Assert.Equal(PlcDownloadPasswordMap.RedactedVariable, Bound("""{"operationId":"a","operation":"safety_login","passwordVariable":"S3cret pass!"}"""));
            Assert.Equal(PlcDownloadPasswordMap.UnsetVariable, Bound("""{"operationId":"a","operation":"safety_login","passwordVariable":"TIA_MCP_TEST_NOT_SET_ANYWHERE"}"""));
            Assert.Throws<JsonException>(() => Bound("""{"operationId":"a","operation":"safety_login","passwordVariable":42}"""));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    // ------------------------------------------------------------------ plc_write domain

    [Fact]
    public void WriteDomain_ConfirmsExactlyTheCallsWithAFailSafeItem()
    {
        var domain = new PlcWriteDomain(null!);
        Assert.False(domain.ConfirmsEveryCall);
        domain.Validate(new[] { Op("create_tag_table", o => o.TableName = "T") }, McpAccessMode.ReadWrite);
        Assert.False(domain.ConfirmsEveryCall);
        domain.Validate(new[] { Op("create_tag_table", o => o.TableName = "T"), Op(PlcSafetyMethods.Logoff) }, McpAccessMode.ReadWrite);
        Assert.True(domain.ConfirmsEveryCall);
    }

    [Fact]
    public void Acceptance_IsTheOnlyAcknowledgeGuardOfPlcWrite_AndCarriesTheNotice()
    {
        var catalog = new WriteGuardCatalog(PlcGuardDefinitions.Definitions);
        Assert.Equal(WriteGuardSeverities.Acknowledge, catalog.Get(PlcGuardDefinitions.SafetyAcceptance).Severity);
        Assert.Equal(WriteGuardSeverities.Block, catalog.Get(PlcGuardDefinitions.SafetyLoginRequired).Severity);
        var fired = PlcSafetyPlanning.Acceptance(Op(PlcSafetyMethods.Login), "PLC_1");
        Assert.Contains(PlcSafetyPolicy.AcceptanceNotice, fired.Message);
        // Read-write asks, full satisfies it by policy; a dry run never asks.
        Assert.Equal(GuardDecisionKind.NeedsUser, GuardDecisions.Decide(new[] { fired }, ConfirmationMode.AskUser, catalog, dryRun: false).Kind);
        Assert.Equal(GuardDecisionKind.Proceed, GuardDecisions.Decide(new[] { fired }, ConfirmationMode.Policy, catalog, dryRun: false).Kind);
        Assert.Equal(GuardDecisionKind.Proceed, GuardDecisions.Decide(new[] { fired }, ConfirmationMode.AskUser, catalog, dryRun: true).Kind);
    }

    // ------------------------------------------------------------------ plc_download F-program configuration

    [Fact]
    public void DownloadPolicy_AdmitsOnlyTheExactSafetyProgramTypeFromTheSafetyNamespace()
    {
        var safety = new[] { new PlcDownloadTypeLevel("Siemens.Engineering.Safety.Download.Configurations", PlcSafetyApiCatalog.SafetyProgramConfigurationType, true) };
        Assert.Equal(PlcSafetyApiCatalog.SafetyProgramConfigurationType, PlcDownloadPolicy.CanonicalConfigurationType(safety));
        Assert.Equal("SafetyProgram", PlcDownloadPolicy.DecisionKey(PlcSafetyApiCatalog.SafetyProgramConfigurationType));
        Assert.Equal("SafetyProgram", PlcDownloadPolicy.DecisionKey(PlcDownloadPolicy.Phases.PreDownload, PlcSafetyApiCatalog.SafetyProgramConfigurationType));
        Assert.Null(PlcDownloadPolicy.DecisionKey(PlcDownloadPolicy.Phases.OnlineLegitimation, PlcSafetyApiCatalog.SafetyProgramConfigurationType));
        var other = new[] { new PlcDownloadTypeLevel("Siemens.Engineering.Safety.Download.Configurations", "Siemens.Engineering.Safety.Download.Configurations.Other", true) };
        Assert.Null(PlcDownloadPolicy.CanonicalConfigurationType(other));
        var internalType = new[] { new PlcDownloadTypeLevel("Siemens.Engineering.Safety.Download.Configurations", PlcSafetyApiCatalog.SafetyProgramConfigurationType, false) };
        Assert.Null(PlcDownloadPolicy.CanonicalConfigurationType(internalType));
        // No built-in default: the configuration aborts with decision_required unless the caller answers it.
        Assert.Null(PlcDownloadPolicy.DefaultSelection(PlcDownloadPolicy.Phases.PreDownload, PlcSafetyApiCatalog.SafetyProgramConfigurationType, true, "PLCSIM"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Download_WithASafetyProgramDecision_AcknowledgesOnPlcsimAndOnHardware(bool simulation)
    {
        var domain = new PlcDownloadWriteDomain(_ => Task.FromResult(WorkerCallResult.Ok("{}")), _ => Task.FromResult(WorkerCallResult.Ok("{}")), _ => null);
        var effect = new PlcDownloadEffects("PLC_1", "Station_1",
            new PlcDownloadPathInfo { Mode = "PN/IE", PcInterface = new PlcDownloadPcInterfaceRefInfo { Name = simulation ? "PLCSIM" : "Intel(R) Ethernet", Number = 1 }, TargetInterface = "1 X1" },
            false, PlcDownloadPolicy.Scopes.HardwareAndSoftware, "", true, simulation,
            new[] { new PlcDownloadDecisionInfo { Type = "SafetyProgram", Selection = "ConsistentDownload" } },
            Array.Empty<PlcDownloadPasswordInfo>(), "decline", Array.Empty<string>());
        var guards = domain.EvaluateGuards(new[] { new PlcDownloadWriteItem() }, new[] { ItemPlan<PlcDownloadEffects>.Resolved(effect) });
        var safety = Assert.Single(guards, guard => guard.Id == PlcDownloadGuardDefinitions.IncludesSafetyProgram);
        Assert.Contains("never replaces the safety acceptance", safety.Message);
        Assert.Equal(!simulation, guards.Any(guard => guard.Id == PlcDownloadGuardDefinitions.ToHardware));
        // full satisfies the acknowledge guards by policy; read-write confirms the call (ConfirmsEveryCall).
        var decision = GuardDecisions.Decide(guards, ConfirmationMode.Policy, WriteGuardRegistration.ProductionCatalog(), dryRun: false);
        Assert.Equal(GuardDecisionKind.Proceed, decision.Kind);
        var without = domain.EvaluateGuards(new[] { new PlcDownloadWriteItem() }, new[] { ItemPlan<PlcDownloadEffects>.Resolved(effect with { Decisions = Array.Empty<PlcDownloadDecisionInfo>() }) });
        Assert.DoesNotContain(without, guard => guard.Id == PlcDownloadGuardDefinitions.IncludesSafetyProgram);
    }
}
