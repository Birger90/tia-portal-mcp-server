using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Library;
using TiaMcpServer.PlcDownload;
using TiaMcpServer.PlcOnline;
using TiaMcpServer.ProjectLifecycle;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Safety.Pipeline;

/// <summary>
/// Caller-supplied text in a confirmation prompt can neither add lines (a probe hid "request operating
/// mode STOP" below 120 line feeds) nor reorder text with bidirectional controls: every confirming
/// domain (library_write included) quotes and escapes it, the pipeline escapes guard lines and puts them
/// first, and a confirmed write refuses such characters in its operationIds.
/// </summary>
public sealed class ConfirmationPromptInjectionTests
{
    /// <summary>The reviewed probe: 120 line feeds, a fake all-clear and a right-to-left override.</summary>
    private static readonly string Probe = "PLC_1" + new string('\n', 120) + "Nothing on the PLC is changed.\r\t\u202Eevil\u2066\u2028\u2029\u200B\uFEFF\u0085";

    [Theory]
    [InlineData("a\nb", "\"a\\nb\"")]
    [InlineData("a\r\tb", "\"a\\r\\tb\"")]
    [InlineData("say \"hi\" \\ there", "\"say \\\"hi\\\" \\\\ there\"")]
    [InlineData("rtl\u202Eoverride", "\"rtl\\u202Eoverride\"")]
    [InlineData("isolate\u2066x\u2069", "\"isolate\\u2066x\\u2069\"")]
    [InlineData("zero\u200Bwidth\uFEFF", "\"zero\\u200Bwidth\\uFEFF\"")]
    [InlineData("line\u2028para\u2029", "\"line\\u2028para\\u2029\"")]
    [InlineData("nel\u0085del\u007F", "\"nel\\u0085del\\u007F\"")]
    [InlineData("Größe 温度 \U0001F600", "\"Größe 温度 \U0001F600\"")]
    public void Quote_EscapesControlFormatAndSeparatorCharacters_AndKeepsOrdinaryText(string value, string expected)
    {
        Assert.Equal(expected, ConfirmationText.Quote(value));
        Assert.False(ConfirmationText.ContainsUnsafeCharacters(ConfirmationText.Quote(value)));
    }

    [Fact]
    public void Quote_EscapesUnpairedSurrogatesAndAstralFormatCharacters()
    {
        Assert.Equal("\"\\uD800x\\uDC00\"", ConfirmationText.Quote("\uD800x\uDC00"));
        // U+E0041 TAG LATIN CAPITAL LETTER A is a format character outside the BMP.
        Assert.Equal("\"a\\uDB40\\uDC41\"", ConfirmationText.Quote("a\U000E0041"));
        Assert.Equal("null", ConfirmationText.Quote(null));
    }

    [Fact]
    public void LineAndBlock_DifferOnlyInLineFeeds_AndNeverAddQuotes()
    {
        Assert.Equal("a\\nb\\u202E \"q\" \\", ConfirmationText.Line("a\nb\u202E \"q\" \\"));
        Assert.Equal("a\nb\\u202E\\r", ConfirmationText.Block("a\nb\u202E\r"));
        Assert.True(ConfirmationText.ContainsUnsafeCharacters("a\tb"));
        Assert.False(ConfirmationText.ContainsUnsafeCharacters("Motor.Speed = 1500 (Größe)"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConfirmedWrite_RefusesUnsafeOperationIds_WithoutPromptOrMutation(bool dryRun)
    {
        var (domain, execution, prompts, audit) = Pipeline(confirmsEveryCall: true);

        var result = await execution.RunAsync(domain, new WriteCall<FakeWriteItem>(new[]
        {
            new FakeWriteItem("ok", Guards: new[] { FakeWriteDomain.AcknowledgeGuard }),
            new FakeWriteItem("x" + new string('\n', 120) + "nothing", Guards: new[] { FakeWriteDomain.AcknowledgeGuard }),
        }, dryRun), new WriteConfirmationContext(Confirmation(prompts)));

        Assert.True(result.IsError);
        var error = Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("error");
        Assert.Equal(WorkerFailureCategories.ValidationError, error.GetProperty("category").GetString());
        Assert.Contains("must not contain control characters", error.GetProperty("message").GetString());
        Assert.Empty(prompts);
        Assert.Equal(0, domain.MutationCount);
        Assert.Single(audit.Records);
    }

    [Fact]
    public async Task Prompt_PutsEscapedGuardLinesBeforeTheDomainDescription()
    {
        // A domain that does not confirm every call still prompts for an acknowledge guard; its message (which here
        // repeats a caller-supplied target) is escaped onto one line, before the domain's description.
        var (domain, execution, prompts, _) = Pipeline(confirmsEveryCall: false);

        await execution.RunAsync(domain, new WriteCall<FakeWriteItem>(new[]
        {
            new FakeWriteItem("x", Guards: new[] { FakeWriteDomain.AcknowledgeGuard }, TargetText: "target-x\r\u202Ey"),
        }, false), new WriteConfirmationContext(Confirmation(prompts)));

        var lines = Assert.Single(prompts).Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Equal("test_acknowledge on target-x\\r\\u202Ey.", lines[0]);
        Assert.StartsWith("Confirm 'fake_write_tool': ", lines[1]);
        Assert.EndsWith("target-x\\r\\u202Ey", lines[1]);
    }

    [Fact]
    public async Task AcknowledgePrompt_OfADomainThatDoesNotConfirmEveryCall_RefusesUnsafeOperationIds_WithoutPromptOrMutation()
    {
        // The pipeline checks the operationIds immediately before any prompt, whatever made the call ask.
        var (domain, execution, prompts, audit) = Pipeline(confirmsEveryCall: false);

        var result = await execution.RunAsync(domain, new WriteCall<FakeWriteItem>(new[]
        {
            new FakeWriteItem("x\r\u202Ey", Guards: new[] { FakeWriteDomain.AcknowledgeGuard }),
        }, false), new WriteConfirmationContext(Confirmation(prompts)));

        Assert.True(result.IsError);
        var error = Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("error");
        Assert.Equal(WorkerFailureCategories.ValidationError, error.GetProperty("category").GetString());
        Assert.Equal(WriteExecution.UnsafeOperationIdMessage, error.GetProperty("message").GetString());
        Assert.Empty(prompts);
        Assert.Equal(0, domain.MutationCount);
        Assert.Single(audit.Records);

        // A call that does not prompt keeps such an operationId (no acknowledge guard here).
        var (quiet, quietExecution, quietPrompts, _) = Pipeline(confirmsEveryCall: false);
        var unprompted = await quietExecution.RunAsync(quiet, new WriteCall<FakeWriteItem>(new[] { new FakeWriteItem("x\r\u202Ey") }, false),
            new WriteConfirmationContext(Confirmation(quietPrompts)));
        Assert.False(unprompted.IsError);
        Assert.Empty(quietPrompts);
        Assert.Equal(1, quiet.MutationCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DomainThatConfirmsOnlyAfterValidate_RefusesUnsafeOperationIds_RightAfterValidate(bool dryRun)
    {
        var (domain, execution, prompts, audit) = Pipeline(confirmsEveryCall: false);
        domain.ConfirmsAfterValidate = true;

        var result = await execution.RunAsync(domain, new WriteCall<FakeWriteItem>(new[] { new FakeWriteItem("x\n\u2066y") }, dryRun),
            new WriteConfirmationContext(Confirmation(prompts)));

        Assert.True(result.IsError);
        Assert.Equal(WriteExecution.UnsafeOperationIdMessage,
            Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("error").GetProperty("message").GetString());
        Assert.Contains("validate", domain.Calls);
        Assert.DoesNotContain("plan", domain.Calls);
        Assert.Empty(prompts);
        Assert.Single(audit.Records);
    }

    [Fact]
    public void DomainsThatDoNotConfirmEveryCall_DefineNoAcknowledgeGuard()
    {
        // Network, HMI and VCI never confirm every call, and library_write confirms only calls with a lifecycle item:
        // none of them may define a guard that prompts on its own (the pipeline would refuse unsafe operationIds there).
        foreach (var definitions in new[]
                 {
                     TiaMcpServer.Network.NetworkGuardDefinitions.Definitions,
                     TiaMcpServer.Hmi.HmiGuardDefinitions.Definitions, TiaMcpServer.Vci.VciGuardDefinitions.Definitions,
                 })
            Assert.DoesNotContain(definitions, definition => definition.Severity == WriteGuardSeverities.Acknowledge);
        // library_write has exactly one (issue #27, a security-relevant TIA Portal setting) and confirms every call containing
        // such an item (ConfirmsEveryCall), so unsafe operationIds are refused for those calls.
        Assert.Equal(new[] { LibraryGuardDefinitions.PortalSecuritySetting },
            LibraryGuardDefinitions.Definitions.Where(definition => definition.Severity == WriteGuardSeverities.Acknowledge).Select(definition => definition.Id));
        var security = new LibraryWriteDomain((_, _) => Task.FromResult(WorkerCallResult.Ok("{}")));
        security.Validate(new[] { new LibraryOperationRequest { OperationId = "s", Operation = "set_portal_setting", SettingsFolderPath = "General/Security", SettingName = "CertificateCheck", SettingValue = "false" } }, TiaMcpServer.Contracts.Safety.McpAccessMode.ReadWrite);
        Assert.True(security.ConfirmsEveryCall);
        // Security review F2: a plain TIA Portal setting confirms too.
        var plain = new LibraryWriteDomain((_, _) => Task.FromResult(WorkerCallResult.Ok("{}")));
        plain.Validate(new[] { new LibraryOperationRequest { OperationId = "p", Operation = "set_portal_setting", SettingsFolderPath = "General", SettingName = "ShowStartView", SettingValue = "false" } }, TiaMcpServer.Contracts.Safety.McpAccessMode.ReadWrite);
        Assert.True(plain.ConfirmsEveryCall);
        // plc_write has two, the fail-safe acceptance guard and the online master secret (issue #16), and confirms every call that
        // fires one (ConfirmsEveryCall is true for a call with a fail-safe or online master secret item), so unsafe operationIds are
        // refused for those calls as for library_write.
        // Security review F8 added the third: automatic acceptance of syslog client certificates.
        // Package G added the online actual-value loads and the project protection.
        Assert.Equal(new[] { TiaMcpServer.Plc.PlcGuardDefinitions.SafetyAcceptance, TiaMcpServer.Plc.PlcGuardDefinitions.MasterSecretOnline, TiaMcpServer.Plc.PlcGuardDefinitions.LoadsActualValuesOnline, TiaMcpServer.Plc.PlcGuardDefinitions.ProtectsProject, TiaMcpServer.Plc.PlcGuardDefinitions.SyslogAutoAcceptsCertificates },
            TiaMcpServer.Plc.PlcGuardDefinitions.Definitions.Where(definition => definition.Severity == WriteGuardSeverities.Acknowledge).Select(definition => definition.Id));
    }

    [Fact]
    public void Lifecycle_QuotesEveryPath_AndStatesTheEffectFirst()
    {
        foreach (var operation in new[] { "open_project", "create_project", "save_project", "save_project_as", "archive_project", "close_project" })
        {
            var item = new LifecycleWriteItem(operation);
            var effect = new LifecycleEffects(Probe, Probe, Probe, null, null, operation == "open_project", false, false, false, Probe, Probe);
            var message = new LifecycleWriteDomain(null!, item).DescribeForConfirmation([item], [ItemPlan<LifecycleEffects>.Resolved(effect)]);

            AssertSafe(message);
            Assert.StartsWith($"Confirm {operation}", message);
            Assert.Contains(ConfirmationText.Quote(Probe), message);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PlcDownload_QuotesProjectAndPathNames_AfterTheTargetKind(bool simulation)
    {
        var path = new PlcDownloadPathInfo
        {
            Mode = Probe,
            PcInterface = new PlcDownloadPcInterfaceRefInfo { Name = Probe, Number = 1 },
            TargetInterface = Probe,
        };
        var effect = new PlcDownloadEffects(Probe, Probe, path, false, "hardware_and_software", "only_changes", false, simulation,
            new[] { new PlcDownloadDecisionInfo { Type = "ResetModule", Selection = "DeleteAll" } },
            new[] { new PlcDownloadPasswordInfo { Type = "ModuleWriteAccessPassword", Variable = "PLC_PW" } }, "decline", new[] { new string('A', 64) });
        var domain = new PlcDownloadWriteDomain(_ => Task.FromResult(WorkerCallResult.Ok("{}")), _ => Task.FromResult(WorkerCallResult.Ok("{}")));

        var message = domain.DescribeForConfirmation(Array.Empty<PlcDownloadWriteItem>(), [ItemPlan<PlcDownloadEffects>.Resolved(effect)]);

        AssertSafe(message);
        var kind = message.IndexOf(simulation ? "PLCSIM simulation interface" : "REAL controller", StringComparison.Ordinal);
        Assert.InRange(kind, 0, message.IndexOf(ConfirmationText.Quote(Probe), StringComparison.Ordinal));
        Assert.Contains("left in STOP afterwards", message);
        Assert.Contains("ResetModule=DeleteAll", message);
        Assert.Contains("ModuleWriteAccessPassword from PLC_PW", message);
        Assert.Contains($"one of these SHA-256 fingerprints (trustedCertificates), otherwise NonTrusted and the download aborts: {new string('A', 64)}.", message);
    }

    [Theory]
    [InlineData(PlcOnlineConnectionActions.GoOnline)]
    [InlineData(PlcOnlineConnectionActions.GoOffline)]
    public void PlcTiaOnline_QuotesProjectAndPathNames_AfterTheAction(string action)
    {
        var path = new PlcConnectionPathInfo
        {
            Mode = Probe,
            PcInterface = new PlcConnectionPcInterfaceRefInfo { Name = Probe, Number = 1 },
            TargetInterface = Probe,
        };
        var effect = new PlcOnlineConnectionEffects(action, Probe, Probe, action == PlcOnlineConnectionActions.GoOnline ? path : null, false, Probe,
            false, 30, new[] { "OnlinePasswordConfiguration" }, "decline", action == PlcOnlineConnectionActions.GoOnline ? new[] { new string('B', 64) } : Array.Empty<string>());
        var domain = new PlcOnlineConnectionWriteDomain(_ => Task.FromResult(WorkerCallResult.Ok("{}")), (_, _) => Task.FromResult(WorkerCallResult.Ok("{}")));

        var message = domain.DescribeForConfirmation(Array.Empty<PlcOnlineConnectionItem>(), [ItemPlan<PlcOnlineConnectionEffects>.Resolved(effect)]);

        AssertSafe(message);
        Assert.StartsWith($"Confirm plc_tia_online {action}:", message);
        Assert.InRange(message.IndexOf("Nothing is", StringComparison.Ordinal), 0,
            message.IndexOf(ConfirmationText.Quote(Probe), StringComparison.Ordinal));
    }

    [Fact]
    public void PlcStationUpload_QuotesTheAddressAndPathNames_AfterTheEffect()
    {
        var path = new PlcConnectionPathInfo { Mode = Probe, PcInterface = new PlcConnectionPcInterfaceRefInfo { Name = Probe, Number = 1 } };
        var effect = new PlcStationUploadEffects(Probe, path, new[] { "UploadSelection" }, Array.Empty<string>(), true, "decline", Array.Empty<string>());
        var domain = new PlcStationUploadWriteDomain(() => Task.FromResult(WorkerCallResult.Ok("{}")), _ => Task.FromResult(WorkerCallResult.Ok("{}")));

        var message = domain.DescribeForConfirmation(Array.Empty<PlcStationUploadWriteItem>(), [ItemPlan<PlcStationUploadEffects>.Resolved(effect)]);

        AssertSafe(message);
        Assert.InRange(message.IndexOf("add it as a new device", StringComparison.Ordinal), 0,
            message.IndexOf(ConfirmationText.Quote(Probe), StringComparison.Ordinal));
    }

    [Fact]
    public void LibraryWrite_QuotesTargetsPathsAndOperationIds_AfterTheEffect()
    {
        var items = new[]
        {
            new LibraryOperationRequest { OperationId = Probe, Operation = LibraryMethods.OpenGlobalLibrary, Path = Probe },
            new LibraryOperationRequest { OperationId = "close", Operation = LibraryMethods.CloseGlobalLibrary, Library = Probe, PortalConfirmations = "accept" },
            new LibraryOperationRequest { OperationId = "rename", Operation = LibraryMethods.RenameType, Library = Probe, TypePath = "A", NewName = "B" },
        };
        var plans = new[]
        {
            ItemPlan<LibraryWriteEffect>.Resolved(new LibraryWriteEffect(LibraryMethods.OpenGlobalLibrary, null, "global library file " + Probe, false, Array.Empty<string>())),
            ItemPlan<LibraryWriteEffect>.DependsOnItem(Probe),
            ItemPlan<LibraryWriteEffect>.Resolved(new LibraryWriteEffect(LibraryMethods.RenameType, Probe, "type /A -> 'B'", false, Array.Empty<string>())),
        };

        var message = new LibraryWriteDomain((_, _) => Task.FromResult(WorkerCallResult.Ok("{}"))).DescribeForConfirmation(items, plans);

        Assert.All(message.Split('\n'), line => Assert.False(ConfirmationText.ContainsUnsafeCharacters(line), line));
        Assert.DoesNotContain("PLC_1\n", message, StringComparison.Ordinal);
        Assert.Contains("PLC_1\\n\\n", message, StringComparison.Ordinal);
        Assert.StartsWith("Confirm library_write: this call opens, creates, retrieves, saves, archives or closes global library files or secondary projects outside the bound project:\n", message);
        var lines = message.Split('\n');
        Assert.StartsWith("1. open_global_library \"global library file PLC_1\\n", lines[1]);
        Assert.EndsWith($"; TIA Portal dialogs: decline; operationId {ConfirmationText.Quote(Probe)}", lines[1]);
        // A dependent item (target unknown until its replan) shows the caller's library, quoted as well, and says why.
        Assert.Equal($"2. close_global_library library {ConfirmationText.Quote(Probe)} (resolved just before its mutation: it depends on an earlier item "
            + "of this call); TIA Portal dialogs: accept; operationId \"close\"", lines[2]);
        Assert.Equal(5, lines.Length);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LibraryWrite_RefusesUnsafeOperationIdsInACallWithALifecycleItem_WithoutPromptOrWorkerCall(bool dryRun)
    {
        var gate = new FakeWriteBindingGate { AccessMode = McpAccessMode.ReadWrite };
        var audit = new RecordingAuditSink(gate);
        var calls = new List<string>();
        var domain = new LibraryWriteDomain((method, _) =>
        {
            calls.Add(method);
            return Task.FromResult(WorkerCallResult.Ok("{}"));
        });
        var prompts = new List<string>();

        var result = await new WriteExecution(gate, audit, WriteGuardRegistration.ProductionCatalog(), TimeProvider.System).RunAsync(domain,
            new WriteCall<LibraryOperationRequest>(new[]
            {
                new LibraryOperationRequest { OperationId = "x" + new string('\n', 120) + "nothing", Operation = LibraryMethods.SaveGlobalLibrary,
                    Library = @"C:\Libraries\Plant\Plant.al21" },
            }, dryRun), new WriteConfirmationContext(Confirmation(prompts)));

        Assert.True(result.IsError);
        var error = Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("error");
        Assert.Equal(WorkerFailureCategories.ValidationError, error.GetProperty("category").GetString());
        Assert.Contains("must not contain control characters", error.GetProperty("message").GetString());
        Assert.Empty(prompts);
        Assert.Empty(calls);
        Assert.Single(audit.Records);
    }

    /// <summary>A scripted library worker: plans name the operation as target; mutations succeed with an empty outcome.</summary>
    private static Func<string, LibraryRequestInfo, Task<WorkerCallResult>> LibraryWorker(List<string> calls) => (method, request) =>
    {
        calls.Add(method);
        object payload = method == LibraryMethods.Plan
            ? new LibraryPlanInfo { Operation = request.Operation, Target = "planned " + request.Operation }
            : new LibraryWriteResultInfo { Operation = LibraryMethods.OperationOf(method)!, Target = "t", PortalConfirmationPolicy = request.PortalConfirmations! };
        return Task.FromResult(WorkerCallResult.Ok(TiaMcpServer.Contracts.Json.WorkerJson.SerializePayload(payload)));
    };

    [Fact]
    public async Task LibraryWrite_ShowsTheUpgradeOfADependentOpenBeforeTheHumanConfirms()
    {
        // close X then open X with upgrade:true: the open depends on the close and has no plan until after the
        // confirmation, so its line is built from the item itself and names OpenWithUpgrade.
        var gate = new FakeWriteBindingGate { AccessMode = McpAccessMode.ReadWrite };
        var calls = new List<string>();
        var prompts = new List<string>();
        var library = @"C:\Libraries\Plant\Plant.al21";

        var result = await new WriteExecution(gate, new RecordingAuditSink(gate), WriteGuardRegistration.ProductionCatalog(), TimeProvider.System).RunAsync(
            new LibraryWriteDomain(LibraryWorker(calls)),
            new WriteCall<LibraryOperationRequest>(new[]
            {
                new LibraryOperationRequest { OperationId = "close", Operation = LibraryMethods.CloseGlobalLibrary, Library = library },
                new LibraryOperationRequest { OperationId = "open", Operation = LibraryMethods.OpenGlobalLibrary, Path = library, Upgrade = true },
            }, false), new WriteConfirmationContext(Confirmation(prompts)));

        Assert.False(result.IsError);
        var prompt = Assert.Single(prompts);
        AssertNoUnsafeCharacters(prompt);
        var lines = prompt.Split('\n');
        Assert.Equal("1. close_global_library \"planned close_global_library\"; TIA Portal dialogs: decline; operationId \"close\"", lines[1]);
        Assert.Equal($"2. open_global_library path {ConfirmationText.Quote(library)} (resolved just before its mutation: it depends on an earlier item "
            + "of this call); OpenWithUpgrade: may upgrade the library file in place on disk; TIA Portal dialogs: decline; operationId \"open\"", lines[2]);
        // The prompt was shown before anything changed; the open was planned again after it, then ran; then the open was read
        // back in the open libraries (K61; the reopen supersedes the close's "not open" read-back).
        Assert.Equal(new[] { LibraryMethods.Plan, LibraryMethods.Plan, "library_close_global_library", LibraryMethods.Plan, "library_open_global_library",
            "library_list_libraries" }, calls);
    }

    [Theory]
    [InlineData(LibraryMethods.OpenGlobalLibrary, null, "ReadWrite", "path \"C:\\\\L\\\\P.al21\" (resolved just before its mutation: it depends on an earlier item of this call); OpenMode ReadWrite: save_global_library overwrites the library file")]
    [InlineData(LibraryMethods.OpenGlobalLibrary, null, null, "path \"C:\\\\L\\\\P.al21\" (resolved just before its mutation: it depends on an earlier item of this call); OpenMode ReadOnly")]
    [InlineData(LibraryMethods.RetrieveGlobalLibrary, @"C:\Out", "ReadWrite", "archive \"C:\\\\L\\\\P.al21\" into \"C:\\\\Out\" (resolved just before its mutation: it depends on an earlier item of this call); OpenMode ReadWrite: save_global_library overwrites the library file")]
    [InlineData(LibraryMethods.CreateGlobalLibrary, @"C:\Out", null, "new library \"New\" in \"C:\\\\Out\" (resolved just before its mutation: it depends on an earlier item of this call)")]
    [InlineData(LibraryMethods.SaveGlobalLibrary, null, null, "library \"C:\\\\L\\\\P.al21\" (resolved just before its mutation: it depends on an earlier item of this call)")]
    public void LibraryWrite_LinesOfDependentLifecycleItems_AreBuiltFromTheItem(string operation, string? directory, string? openMode, string expected)
    {
        var item = new LibraryOperationRequest
        {
            OperationId = "x", Operation = operation, OpenMode = openMode, TargetDirectory = directory,
            Path = operation is LibraryMethods.OpenGlobalLibrary or LibraryMethods.RetrieveGlobalLibrary ? @"C:\L\P.al21" : null,
            Library = operation == LibraryMethods.SaveGlobalLibrary ? @"C:\L\P.al21" : null,
            Name = operation == LibraryMethods.CreateGlobalLibrary ? "New" : null,
        };
        Assert.Equal(expected + "; TIA Portal dialogs: decline; operationId \"x\"", LibraryWriteDomain.LifecycleLine(item, effect: null));
        // A resolved item keeps its planned target and still states the requested open mode.
        var resolved = LibraryWriteDomain.LifecycleLine(item, new LibraryWriteEffect(operation, null, "planned", false, Array.Empty<string>()));
        Assert.StartsWith("\"planned\"", resolved, StringComparison.Ordinal);
        Assert.Equal(expected.Contains("OpenMode", StringComparison.Ordinal), resolved.Contains("; OpenMode ", StringComparison.Ordinal));
    }

    private static void AssertNoUnsafeCharacters(string message)
        => Assert.All(message.Split('\n'), line => Assert.False(ConfirmationText.ContainsUnsafeCharacters(line), line));

    /// <summary>One line, no raw unsafe character, and the probe only in its quoted, escaped form.</summary>
    private static void AssertSafe(string message)
    {
        Assert.False(ConfirmationText.ContainsUnsafeCharacters(message), message);
        Assert.DoesNotContain("PLC_1\n", message, StringComparison.Ordinal);
        Assert.Contains("PLC_1\\n\\n", message, StringComparison.Ordinal);
    }

    private static (FakeWriteDomain Domain, WriteExecution Execution, List<string> Prompts, RecordingAuditSink Audit) Pipeline(bool confirmsEveryCall)
    {
        var gate = new FakeWriteBindingGate { AccessMode = McpAccessMode.ReadWrite };
        var audit = new RecordingAuditSink(gate);
        var domain = new FakeWriteDomain(gate) { ConfirmsEveryCall = confirmsEveryCall };
        return (domain, new WriteExecution(gate, audit, FakeWriteDomain.Catalog, TimeProvider.System), new List<string>(), audit);
    }

    private static UserConfirmation Confirmation(List<string> prompts) => new(true, (request, _) =>
    {
        prompts.Add(request.Message);
        return ValueTask.FromResult(new ElicitResult
        {
            Action = "accept",
            Content = new Dictionary<string, JsonElement> { ["confirm"] = JsonSerializer.SerializeToElement(true) },
        });
    }, TimeSpan.FromSeconds(5));
}
