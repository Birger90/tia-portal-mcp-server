using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.Library;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Library;

/// <summary>library_write planning, dependency ordering, confirmation scope and guards through the worker seam.</summary>
public sealed class LibraryWriteDomainTests
{
    private const string Plant = @"C:\Libraries\Plant\Plant.al21";

    private sealed class Worker
    {
        public List<(string Method, LibraryRequestInfo Request)> Calls { get; } = new();
        public Func<LibraryRequestInfo, WorkerCallResult>? Plan { get; set; }

        public Task<WorkerCallResult> Call(string method, LibraryRequestInfo request)
        {
            Calls.Add((method, request));
            if (method == LibraryMethods.Plan)
                return Task.FromResult(Plan?.Invoke(request) ?? Ok(new LibraryPlanInfo
                {
                    Operation = request.Operation, Target = "target of " + request.Operation,
                    Library = request.Library is null ? null : new LibraryRefInfo { Library = request.Library, Kind = "user" },
                    Consequences = new List<string> { "consequence of " + request.Operation },
                }));
            // K61 read-backs: the open global libraries and each library's root master copies as modeled here.
            if (method == LibraryMethods.ForOperation(LibraryMethods.ListLibraries))
                return Task.FromResult(Ok(new LibraryListInfo
                {
                    Libraries = new[] { new LibraryRefInfo { Library = "project", Kind = "project" } }
                        .Concat(OpenLibraries.Select(path => new LibraryRefInfo { Library = path, Kind = "user", Path = path })).ToList(),
                }));
            if (method == LibraryMethods.ForOperation(LibraryMethods.GetLibraryTree))
            {
                var names = MasterCopies.TryGetValue(request.Library!, out var listed) ? listed : new List<string>();
                var entries = names.Select(name => new LibraryTreeEntryInfo { Kind = LibraryPolicy.EntryKinds.MasterCopy, Path = "/" + name, Name = name, Depth = 1 }).ToList();
                return Task.FromResult(Ok(new LibraryTreeInfo
                {
                    Library = new LibraryRefInfo { Library = request.Library!, Kind = "user" }, Content = request.Content!, Offset = request.Offset, Limit = request.Limit,
                    Total = entries.Count, Returned = entries.Count, Complete = true, Entries = entries,
                }));
            }
            return Task.FromResult(Ok(new LibraryWriteResultInfo { Operation = LibraryMethods.OperationOf(method)!, Target = "t" }));
        }

        public List<string> OpenLibraries { get; } = new();
        public Dictionary<string, List<string>> MasterCopies { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static WorkerCallResult Ok(object payload) => new(true, WorkerJson.SerializePayload(payload), null, null, Array.Empty<string>());

    private static LibraryOperationRequest Item(string id, string operation, string? library = null, string? path = null) => new()
    {
        OperationId = id, Operation = operation, Library = library, Path = path,
    };

    [Fact]
    public async Task Planning_ItemsOnALibraryAnEarlierItemTouches_DependOnIt()
    {
        var worker = new Worker();
        var domain = new LibraryWriteDomain(worker.Call);
        var items = new[]
        {
            Item("open", LibraryMethods.OpenGlobalLibrary, path: Plant),
            new LibraryOperationRequest { OperationId = "copy", Operation = LibraryMethods.CopyMasterCopy, Library = "project", MasterCopyName = "Valve", TargetLibrary = @"c:\libraries\plant\PLANT.al21" },
            Item("rename", LibraryMethods.RenameType, "project"),
            Item("other", LibraryMethods.RenameType, @"C:\Other\Other.al21"),
        };

        var plan = await domain.PlanAsync(items);

        Assert.True(plan.Success);
        Assert.Null(plan.Items[0].DependsOn);
        Assert.Equal("open", plan.Items[1].DependsOn);
        Assert.Equal("copy", plan.Items[2].DependsOn);
        Assert.Null(plan.Items[3].DependsOn);
        Assert.Equal(new[] { "open", "other" }, worker.Calls.Where(c => c.Method == LibraryMethods.Plan).Select(c => items.Single(i => i.Operation == c.Request.Operation && (i.Library ?? i.Path) == (c.Request.Library ?? c.Request.Path)).OperationId));
    }

    [Fact]
    public async Task Confirmation_CoversExactlyCallsWithALifecycleItem()
    {
        var domain = new LibraryWriteDomain(new Worker().Call);
        domain.Validate(new[] { Item("rename", LibraryMethods.RenameType, "project") }, McpAccessMode.ReadWrite);
        Assert.False(domain.ConfirmsEveryCall);

        var items = new[] { Item("save", LibraryMethods.SaveGlobalLibrary, Plant), Item("rename", LibraryMethods.RenameType, Plant) };
        domain.Validate(items, McpAccessMode.ReadWrite);
        Assert.True(domain.ConfirmsEveryCall);
        var plan = await domain.PlanAsync(items);
        var text = domain.DescribeForConfirmation(items, plan.Items);
        Assert.Contains("1. save_global_library \"target of save_global_library\"; TIA Portal dialogs: decline; operationId \"save\"", text);
        Assert.Contains("1 library content item(s)", text);
        Assert.DoesNotContain("rename_type", text);
    }

    [Fact]
    public async Task PlanFailure_NamesTheItem_AndBadEvidenceIsAProtocolError()
    {
        var worker = new Worker { Plan = _ => new WorkerCallResult(false, "{}", "No master copy named 'X'.", WorkerFailureCategories.TargetNotFound, Array.Empty<string>()) };
        var plan = await new LibraryWriteDomain(worker.Call).PlanAsync(new[] { Item("delete", LibraryMethods.DeleteMasterCopy, "project") });
        Assert.False(plan.Success);
        Assert.Equal(WorkerFailureCategories.TargetNotFound, plan.Error!.Category);
        Assert.StartsWith("Operation 'delete': No master copy", plan.Error.Message);

        worker.Plan = request => Ok(new LibraryPlanInfo { Operation = "something_else", Target = "x" });
        var mismatched = await new LibraryWriteDomain(worker.Call).ReplanAsync(Item("delete", LibraryMethods.DeleteMasterCopy, "project"));
        Assert.Equal(WorkerFailureCategories.ProtocolError, mismatched.Error!.Category);
    }

    public static TheoryData<string, string?, string?, string[]> GuardCases => new()
    {
        { LibraryMethods.OpenGlobalLibrary, null, null, Array.Empty<string>() },
        { LibraryMethods.OpenGlobalLibrary, "ReadWrite", null, new[] { LibraryGuardDefinitions.WritesFiles } },
        { LibraryMethods.SaveGlobalLibrary, null, Plant, new[] { LibraryGuardDefinitions.WritesFiles } },
        { LibraryMethods.CloseGlobalLibrary, null, Plant, new[] { LibraryGuardDefinitions.CloseUnsaved } },
        { LibraryMethods.DeleteMasterCopy, null, "project", new[] { LibraryGuardDefinitions.Deletes } },
        { LibraryMethods.DeleteTypeVersion, null, Plant, new[] { LibraryGuardDefinitions.Deletes, LibraryGuardDefinitions.GlobalUnsaved } },
        { LibraryMethods.InstantiateTypeVersion, null, Plant, new[] { LibraryGuardDefinitions.SyncsProjectLibrary } },
        { LibraryMethods.InstantiateTypeVersion, null, "project", Array.Empty<string>() },
        { LibraryMethods.UpdateProject, null, Plant, new[] { LibraryGuardDefinitions.UpdateDefaults } },
        { LibraryMethods.RenameType, null, "project", Array.Empty<string>() },
    };

    [Theory]
    [MemberData(nameof(GuardCases))]
    public void Guards_AreInformativeAndFollowTheItem(string operation, string? openMode, string? library, string[] expected)
    {
        var item = new LibraryOperationRequest { OperationId = "a", Operation = operation, OpenMode = openMode, Library = library, Path = Plant };
        var guards = LibraryWriteDomain.Guards(item, new LibraryPlanInfo { Operation = operation, Target = "target", Consequences = new List<string> { "c" } });
        Assert.Equal(expected, guards.Select(g => g.Id));
        var catalog = WriteGuardRegistration.ProductionCatalog();
        Assert.All(guards, guard => Assert.Equal(WriteGuardSeverities.Info, catalog.Get(guard.Id).Severity));
    }

    [Fact]
    public void Guards_AlreadyOpenAndUpgradeAreReported()
    {
        var open = new LibraryOperationRequest { OperationId = "a", Operation = LibraryMethods.OpenGlobalLibrary, Path = Plant, Upgrade = true };
        Assert.Equal(new[] { LibraryGuardDefinitions.UpgradesInPlace, LibraryGuardDefinitions.WritesFiles },
            LibraryWriteDomain.Guards(open, new LibraryPlanInfo { Operation = open.Operation, Target = "t" }).Select(g => g.Id));
        Assert.Equal(new[] { LibraryGuardDefinitions.AlreadyOpen },
            LibraryWriteDomain.Guards(open, new LibraryPlanInfo { Operation = open.Operation, Target = "t", AlreadyOpen = true }).Select(g => g.Id));
    }

    [Fact]
    public void GuardCatalog_IsInformative()
    {
        // The one acknowledge guard is the security-relevant TIA Portal setting of issue #27; every other guard informs.
        Assert.Equal(new[] { LibraryGuardDefinitions.PortalSecuritySetting },
            LibraryGuardDefinitions.Definitions.Where(definition => definition.Severity != WriteGuardSeverities.Info).Select(definition => definition.Id));
        Assert.Equal(WriteGuardSeverities.Info, WriteGuardRegistration.ProductionCatalog().Get(LibraryGuardDefinitions.CloseUnsavedChanges).Severity);
    }

    private static ItemPlan<LibraryWriteEffect> Planned(bool alreadyOpen = false)
        => ItemPlan<LibraryWriteEffect>.Resolved(new LibraryWriteEffect("op", null, "target", alreadyOpen, Array.Empty<string>()));

    private static string[] UnsavedCloses(IReadOnlyDictionary<string, LibraryUnsavedChanges.Pending> known, params LibraryOperationRequest[] items)
        => LibraryWriteDomain.UnsavedCloseGuards(items, items.Select(item => Planned()).ToArray(), known)
            .Select(guard => $"{guard.Id}@{guard.OperationId}").ToArray();

    private static readonly IReadOnlyDictionary<string, LibraryUnsavedChanges.Pending> NoneKnown = new Dictionary<string, LibraryUnsavedChanges.Pending>();

    [Fact]
    public void UnsavedClose_IsBlockedAfterAnUnsavedChangeOfThisServer_InCallOrder()
    {
        var rename = new LibraryOperationRequest { OperationId = "rename", Operation = LibraryMethods.RenameType, Library = Plant, TypePath = "A", NewName = "B" };
        var close = Item("close", LibraryMethods.CloseGlobalLibrary, Plant.ToLowerInvariant());
        var save = Item("save", LibraryMethods.SaveGlobalLibrary, Plant);
        var copy = new LibraryOperationRequest { OperationId = "copy", Operation = LibraryMethods.CopyMasterCopy, Library = "project", MasterCopyName = "Valve", TargetLibrary = Plant };
        var place = new LibraryOperationRequest { OperationId = "place", Operation = LibraryMethods.InstantiateMasterCopy, Library = Plant, MasterCopyName = "Pump" };

        Assert.Equal(new[] { "library_close_unsaved_changes@close" }, UnsavedCloses(NoneKnown, rename, close));
        Assert.Equal(new[] { "library_close_unsaved_changes@close" }, UnsavedCloses(NoneKnown, copy, close));
        Assert.Empty(UnsavedCloses(NoneKnown, rename, save, close));
        Assert.Empty(UnsavedCloses(NoneKnown, place, close));
        Assert.Empty(UnsavedCloses(NoneKnown, close, rename));

        var guard = LibraryWriteDomain.UnsavedCloseGuards(new[] { rename, close }, new[] { Planned(), Planned() }, NoneKnown).Single();
        Assert.Contains("rename_type (rename) earlier in this call", guard.Message, StringComparison.Ordinal);
        Assert.Contains("save_global_library", guard.Message, StringComparison.Ordinal);
    }

    private const int PortalA = 4100;
    private const int PortalB = 4200;

    [Fact]
    public void UnsavedClose_UsesTheLedgerOfEarlierCalls_AndAFreshOpenStartsClean()
    {
        var ledger = new LibraryUnsavedChanges();
        var rename = new LibraryOperationRequest { OperationId = "r1", Operation = LibraryMethods.RenameType, Library = Plant, TypePath = "A", NewName = "B" };
        ledger.Observe(PortalA, rename, Ok(new LibraryWriteResultInfo()), Succeeded(rename));
        var close = Item("close", LibraryMethods.CloseGlobalLibrary, Plant);

        var guard = LibraryWriteDomain.UnsavedCloseGuards(new[] { close }, new[] { Planned() }, ledger.Snapshot(PortalA)).Single();
        Assert.Contains("rename_type (r1) in an earlier call", guard.Message, StringComparison.Ordinal);
        var open = Item("open", LibraryMethods.OpenGlobalLibrary, path: Plant);
        Assert.Empty(LibraryWriteDomain.UnsavedCloseGuards(new[] { open, close }, new[] { Planned(alreadyOpen: false), Planned() }, ledger.Snapshot(PortalA)));
        Assert.Single(LibraryWriteDomain.UnsavedCloseGuards(new[] { open, close }, new[] { Planned(alreadyOpen: true), Planned() }, ledger.Snapshot(PortalA)));
    }

    [Fact]
    public void Ledger_IsKeyedByTheTiaPortalProcess_AndAnUnknownProcessIsConservative()
    {
        var ledger = new LibraryUnsavedChanges();
        var key = LibraryCatalog.Key(Plant)!;
        var rename = new LibraryOperationRequest { OperationId = "r", Operation = LibraryMethods.RenameType, Library = Plant, TypePath = "A", NewName = "B" };
        var save = Item("s", LibraryMethods.SaveGlobalLibrary, Plant);
        var close = Item("c", LibraryMethods.CloseGlobalLibrary, Plant);
        var open = Item("o", LibraryMethods.OpenGlobalLibrary, path: Plant);

        // A change made in Portal A belongs to A: the same file open in Portal B has no unsaved change of this server.
        ledger.Observe(PortalA, rename, Ok(new LibraryWriteResultInfo()), Succeeded(rename));
        Assert.True(ledger.Snapshot(PortalA).ContainsKey(key));
        Assert.Empty(ledger.Snapshot(PortalB));
        Assert.True(ledger.Snapshot(null).ContainsKey(key));

        // Saving, closing or freshly opening the file in B forgets nothing of A.
        ledger.Observe(PortalB, save, Ok(new LibraryWriteResultInfo()), Succeeded(save));
        ledger.Observe(PortalB, close, Ok(new LibraryWriteResultInfo()), Succeeded(close));
        ledger.Observe(PortalB, open, Ok(new LibraryWriteResultInfo()), Succeeded(open, new { alreadyOpen = false, library = new { library = Plant } }));
        Assert.True(ledger.Snapshot(PortalA).ContainsKey(key));

        // With an unknown process nothing is forgotten either.
        ledger.Observe(null, save, Ok(new LibraryWriteResultInfo()), Succeeded(save));
        Assert.True(ledger.Snapshot(PortalA).ContainsKey(key));
        ledger.Observe(PortalA, save, Ok(new LibraryWriteResultInfo()), Succeeded(save));
        Assert.Empty(ledger.Snapshot(PortalA));

        // A change recorded without a known process matches every process, and no process's save forgets it (it cannot be attributed).
        ledger.Observe(null, rename, Ok(new LibraryWriteResultInfo()), Succeeded(rename));
        Assert.True(ledger.Snapshot(PortalA).ContainsKey(key));
        Assert.True(ledger.Snapshot(PortalB).ContainsKey(key));
        ledger.Observe(PortalB, save, Ok(new LibraryWriteResultInfo()), Succeeded(save));
        Assert.True(ledger.Snapshot(PortalB).ContainsKey(key));

        // Entries of A and of an unknown process for one library are merged for A.
        var second = new LibraryOperationRequest { OperationId = "r2", Operation = LibraryMethods.RenameType, Library = Plant, TypePath = "A", NewName = "C" };
        ledger.Observe(PortalA, second, Ok(new LibraryWriteResultInfo()), Succeeded(second));
        var merged = ledger.Snapshot(PortalA)[key];
        Assert.Equal(2, merged.Count);
        Assert.Equal(new[] { "rename_type (r2)", "rename_type (r)" }, merged.Changes);
    }

    [Theory]
    [InlineData(PortalA, true)]
    [InlineData(PortalB, false)]
    [InlineData(null, true)]
    public async Task Domain_RefusesACloseOnlyInThePortalWhereItsChangeWasMade(int? closingPortal, bool refused)
    {
        var worker = new Worker();
        var ledger = new LibraryUnsavedChanges();
        var rename = new LibraryOperationRequest { OperationId = "r", Operation = LibraryMethods.RenameType, Library = Plant, TypePath = "A", NewName = "B" };
        var inA = new LibraryWriteDomain(worker.Call, ledger, () => PortalA);
        await inA.PlanAsync(new[] { rename });
        Assert.Equal(OperationBatchStatus.Succeeded, inA.Project(rename, await inA.MutateAsync(rename)).Status);

        var closing = new LibraryWriteDomain(worker.Call, ledger, () => closingPortal);
        var items = new[] { Item("close", LibraryMethods.CloseGlobalLibrary, Plant) };
        var plan = await closing.PlanAsync(items);
        Assert.Equal(refused, closing.EvaluateGuards(items, plan.Items).Any(guard => guard.Id == LibraryGuardDefinitions.CloseUnsavedChanges));
    }

    private static StructuredOperationItem Succeeded(LibraryOperationRequest item, object? result = null)
        => new(item.OperationId, item.Operation, OperationBatchStatus.Succeeded, CanonicalJson.ToElement(result ?? new { }), null, null, null, Array.Empty<string>());

    private static StructuredOperationItem FailedWith(LibraryOperationRequest item, string category) => StructuredOperationItem.Failed(item, category, "failed", Array.Empty<string>());

    [Fact]
    public void Ledger_RecordsPossibleChanges_AndForgetsThemOnSaveCloseAndFreshOpen()
    {
        var ledger = new LibraryUnsavedChanges();
        var key = LibraryCatalog.Key(Plant)!;
        var rename = new LibraryOperationRequest { OperationId = "r", Operation = LibraryMethods.RenameType, Library = Plant, TypePath = "A", NewName = "B" };
        var failed = new WorkerCallResult(false, "{}", "failed", WorkerFailureCategories.EngineeringRejected, Array.Empty<string>());

        // Refusals before the Siemens call changed nothing; engineering_rejected does not prove that (the mutation flag spans
        // the whole worker method), so it is recorded as a possible change.
        ledger.Observe(PortalA, rename, failed, FailedWith(rename, WorkerFailureCategories.TargetNotFound));
        ledger.Observe(PortalA, rename, failed, FailedWith(rename, WorkerFailureCategories.UnsupportedCapability));
        Assert.Empty(ledger.Snapshot(PortalA));
        ledger.Observe(PortalA, rename, failed, FailedWith(rename, WorkerFailureCategories.EngineeringRejected));
        Assert.Equal(new[] { "rename_type (r, failed after a possible change)" }, ledger.Snapshot(PortalA)[key].Changes);
        ledger.Observe(PortalA, rename, failed, FailedWith(rename, WorkerFailureCategories.PostconditionFailed));
        Assert.Equal(2, ledger.Snapshot(PortalA)[key].Count);

        var save = Item("s", LibraryMethods.SaveGlobalLibrary, Plant.ToLowerInvariant());
        ledger.Observe(PortalA, save, failed, FailedWith(save, WorkerFailureCategories.WorkerOperationFailed));
        Assert.True(ledger.Snapshot(PortalA).ContainsKey(key));
        ledger.Observe(PortalA, save, Ok(new LibraryWriteResultInfo()), Succeeded(save));
        Assert.Empty(ledger.Snapshot(PortalA));

        // A binding or identity failure detected after the worker answered followed the mutation.
        ledger.Observe(PortalA, rename, failed with { IsPostOperationFailure = true }, FailedWith(rename, WorkerFailureCategories.BindingConflict));
        Assert.Equal(1, ledger.Snapshot(PortalA)[key].Count);
        // An open the worker reports as already open (also when Open returned a library that was open before) keeps the entry.
        var open = Item("o", LibraryMethods.OpenGlobalLibrary, path: Plant);
        ledger.Observe(PortalA, open, Ok(new LibraryWriteResultInfo()), Succeeded(open, new { alreadyOpen = true, library = new { library = Plant } }));
        Assert.True(ledger.Snapshot(PortalA).ContainsKey(key));
        ledger.Observe(PortalA, open, Ok(new LibraryWriteResultInfo()), Succeeded(open, new { alreadyOpen = false, library = new { library = Plant } }));
        Assert.Empty(ledger.Snapshot(PortalA));

        var copy = new LibraryOperationRequest { OperationId = "c", Operation = LibraryMethods.CopyMasterCopy, Library = "project", MasterCopyName = "Valve", TargetLibrary = Plant };
        ledger.Observe(PortalA, copy, Ok(new LibraryWriteResultInfo()), Succeeded(copy));
        var close = Item("x", LibraryMethods.CloseGlobalLibrary, Plant);
        ledger.Observe(PortalA, close, Ok(new LibraryWriteResultInfo()), Succeeded(close));
        Assert.Empty(ledger.Snapshot(PortalA));
    }

    [Fact]
    public async Task Domain_BlocksACloseAfterItsOwnUnsavedChange_InALaterCall()
    {
        var worker = new Worker();
        var ledger = new LibraryUnsavedChanges();
        var rename = new LibraryOperationRequest { OperationId = "r", Operation = LibraryMethods.RenameType, Library = Plant, TypePath = "A", NewName = "B" };
        var first = new LibraryWriteDomain(worker.Call, ledger);
        Assert.Equal(OperationBatchStatus.Succeeded, first.Project(rename, await first.MutateAsync(rename)).Status);

        var second = new LibraryWriteDomain(worker.Call, ledger);
        var items = new[] { Item("close", LibraryMethods.CloseGlobalLibrary, Plant) };
        var plan = await second.PlanAsync(items);
        var guards = second.EvaluateGuards(items, plan.Items);
        Assert.Contains(guards, guard => guard.Id == LibraryGuardDefinitions.CloseUnsavedChanges);
        Assert.Contains(guards, guard => guard.Id == LibraryGuardDefinitions.CloseUnsaved);
    }

    private static WorkerCallResult Outcome(string policy, int observed, params string[] answers)
        => Ok(new LibraryWriteResultInfo
        {
            Operation = LibraryMethods.CloseGlobalLibrary, Target = "close", PortalConfirmationPolicy = policy, PortalConfirmationsObserved = observed,
            PortalConfirmationsComplete = observed == answers.Length,
            PortalConfirmations = answers.Select(answer => new PlcDownloadPortalConfirmationInfo { Type = "Siemens.Engineering.ConfirmationEventArgs", Text = "Save?", Answer = answer }).ToList(),
        });

    [Fact]
    public void ProjectWrite_DeclinedConfirmationsFailTheItem_AcceptedOnesWarn_EvidenceMustMatchThePolicy()
    {
        var decline = Item("close", LibraryMethods.CloseGlobalLibrary, Plant);
        var accept = new LibraryOperationRequest { OperationId = "close", Operation = LibraryMethods.CloseGlobalLibrary, Library = Plant, PortalConfirmations = "accept" };

        var declined = LibraryPayloadContract.ProjectWrite(decline, Outcome("decline", 1, "No"));
        Assert.Equal((OperationBatchStatus.Failed, WorkerFailureCategories.WorkerOperationFailed), (declined.Status, declined.Failure!.Category));
        Assert.Contains("answered each one No", declined.Failure.Message, StringComparison.Ordinal);
        Assert.Equal("No", declined.Result!.Value.GetProperty("portalConfirmations")[0].GetProperty("answer").GetString());

        var accepted = LibraryPayloadContract.ProjectWrite(accept, Outcome("accept", 1, "Yes"));
        Assert.Equal(OperationBatchStatus.Succeeded, accepted.Status);
        Assert.Contains(accepted.Warnings, warning => warning.Contains("answered Yes", StringComparison.Ordinal));
        Assert.Equal(OperationBatchStatus.Succeeded, LibraryPayloadContract.ProjectWrite(decline, Outcome("decline", 0)).Status);

        Assert.Equal(WorkerFailureCategories.ProtocolError, LibraryPayloadContract.ProjectWrite(decline, Outcome("accept", 1, "Yes")).Failure!.Category);
        Assert.Equal(WorkerFailureCategories.ProtocolError, LibraryPayloadContract.ProjectWrite(accept, Outcome("accept", 1, "No")).Failure!.Category);
        Assert.Equal(WorkerFailureCategories.ProtocolError, LibraryPayloadContract.ProjectWrite(decline, Outcome("decline", 1)).Failure!.Category);
    }

    [Fact]
    public void ProjectWrite_AFailureKeepsItsEvidence_AndRejectsMalformedEvidence()
    {
        var item = Item("close", LibraryMethods.CloseGlobalLibrary, Plant);
        var evidence = Outcome("decline", 1, "No").Payload;
        var failure = new WorkerCallResult(false, "{}", "TIA Portal rejected the operation: no.", WorkerFailureCategories.EngineeringRejected, Array.Empty<string>())
            { PlcDownloadOutcome = evidence };

        var projected = LibraryPayloadContract.ProjectWrite(item, failure);
        Assert.Equal((OperationBatchStatus.Failed, WorkerFailureCategories.EngineeringRejected), (projected.Status, projected.Failure!.Category));
        Assert.Equal(1, projected.Result!.Value.GetProperty("portalConfirmationsObserved").GetInt32());

        var malformed = LibraryPayloadContract.ProjectWrite(item, failure with { PlcDownloadOutcome = "{\"operation\":\"close_global_library\"}" });
        Assert.Equal(WorkerFailureCategories.ProtocolError, malformed.Failure!.Category);
        Assert.Null(malformed.Result);
        Assert.Null(LibraryPayloadContract.ProjectWrite(item, failure with { PlcDownloadOutcome = null }).Result);
    }

    [Fact]
    public async Task PlanningWarnings_ReachTheRootWarnings()
    {
        // A late dialog answered by an earlier item's lingering scope is reported on the next worker response,
        // which can be a planning response.
        var worker = new Worker
        {
            Plan = request => new WorkerCallResult(true, WorkerJson.SerializePayload(new LibraryPlanInfo { Operation = request.Operation, Target = "t" }),
                null, null, new[] { "1 TIA Portal confirmation dialog(s) were raised after library_close_global_library returned" }),
        };
        var domain = new LibraryWriteDomain(worker.Call);
        var items = new[] { Item("rename", LibraryMethods.RenameType, "project") };
        domain.Validate(items, McpAccessMode.ReadWrite);
        await domain.PlanAsync(items);
        var response = domain.Compose(new WriteReport<LibraryWriteEffect, WriteVerification>(WritePhases.Preview, true, null,
            new[] { "pipeline warning" }, Array.Empty<WriteGuardReport>(), Array.Empty<WriteEffect<LibraryWriteEffect>>(), null, null));
        Assert.Equal(new[] { "pipeline warning", "1 TIA Portal confirmation dialog(s) were raised after library_close_global_library returned" }, response.Warnings);
    }

    [Fact]
    public async Task UnsafeOperationIds_AreRefusedByThePipeline_OnlyInCallsThatConfirm()
    {
        // The domain no longer duplicates the rule: WriteRun applies it right after Validate, when ConfirmsEveryCall is known.
        var domain = new LibraryWriteDomain(new Worker().Call);
        var unsafeId = "x" + new string('\n', 120) + "nothing";
        var rename = new LibraryOperationRequest { OperationId = unsafeId, Operation = LibraryMethods.RenameType, Library = "project", TypePath = "A", NewName = "B" };
        Assert.True(domain.Validate(new[] { Item("ok", LibraryMethods.SaveGlobalLibrary, Plant), rename }, McpAccessMode.ReadWrite).IsValid);
        Assert.True(domain.ConfirmsEveryCall);
        Assert.True(domain.Validate(new[] { rename }, McpAccessMode.ReadWrite).IsValid);
        Assert.False(domain.ConfirmsEveryCall);

        var gate = new TiaMcpServer.Tests.Safety.Pipeline.FakeWriteBindingGate { AccessMode = McpAccessMode.ReadWrite };
        var execution = new WriteExecution(gate, new TiaMcpServer.Tests.Safety.Pipeline.RecordingAuditSink(gate), WriteGuardRegistration.ProductionCatalog(), TimeProvider.System);
        var worker = new Worker();
        var refused = await execution.RunAsync(new LibraryWriteDomain(worker.Call),
            new WriteCall<LibraryOperationRequest>(new[] { Item("ok", LibraryMethods.SaveGlobalLibrary, Plant), rename }, false));
        Assert.True(refused.IsError);
        Assert.Equal(WriteExecution.UnsafeOperationIdMessage, Assert.IsType<System.Text.Json.JsonElement>(refused.StructuredContent)
            .GetProperty("error").GetProperty("message").GetString());
        Assert.Empty(worker.Calls);

        // A content-only call never prompts and keeps its operationIds.
        var content = await execution.RunAsync(new LibraryWriteDomain(worker.Call), new WriteCall<LibraryOperationRequest>(new[] { rename }, false));
        Assert.False(content.IsError);
        Assert.Contains(worker.Calls, call => call.Method == "library_rename_type");
    }

    [Fact]
    public async Task Planning_StatesTheConfirmationPolicy_InEffectsAndPrompt()
    {
        var domain = new LibraryWriteDomain(new Worker().Call);
        var items = new[]
        {
            new LibraryOperationRequest { OperationId = "close", Operation = LibraryMethods.CloseGlobalLibrary, Library = Plant, PortalConfirmations = "accept" },
            new LibraryOperationRequest { OperationId = "other", Operation = LibraryMethods.CloseGlobalLibrary, Library = @"C:\Other\Other.al21" },
        };
        domain.Validate(items, McpAccessMode.ReadWrite);
        var plan = await domain.PlanAsync(items);

        Assert.Equal("TIA Portal confirmation dialogs raised by this item are answered Yes and recorded (portalConfirmations: accept).", plan.Items[0].Effect!.Consequences[^1]);
        Assert.Contains("answered No, recorded and fail the item", plan.Items[1].Effect!.Consequences[^1], StringComparison.Ordinal);
        var text = domain.DescribeForConfirmation(items, plan.Items);
        Assert.Contains("TIA Portal dialogs: accept; operationId \"close\"", text, StringComparison.Ordinal);
        Assert.Contains("TIA Portal dialogs: decline; operationId \"other\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verification_AnUnverifiedWorkerResult_IsNotVerified_SupersededAndFailedEntriesStay()
    {
        StructuredOperationItem Completed(string id, string operation, object result)
            => new(id, operation, OperationBatchStatus.Succeeded, CanonicalJson.ToElement(result), null, null, null, Array.Empty<string>());
        var release = new LibraryOperationRequest { OperationId = "release", Operation = LibraryMethods.ReleaseTypeVersion, Library = "project", TypePath = "/A", VersionNumber = "2.0" };
        var edit = new LibraryOperationRequest { OperationId = "edit", Operation = LibraryMethods.EditTypeVersion, Library = "project", TypePath = "/B" };
        var place = new LibraryOperationRequest { OperationId = "place", Operation = LibraryMethods.InstantiateMasterCopy, Library = "project", MasterCopyName = "V",
            Target = new LibraryTargetInfo { Kind = "devices" } };
        var replace = new LibraryOperationRequest { OperationId = "again", Operation = LibraryMethods.InstantiateMasterCopy, Library = "project", MasterCopyName = "V",
            Target = new LibraryTargetInfo { Kind = "devices" } };
        var items = new[] { release, edit, place, replace };
        var domain = new LibraryWriteDomain(new Worker().Call);
        domain.Validate(items, McpAccessMode.Full);
        var batch = StructuredOperationBatch.FromItems(new List<StructuredOperationItem>
        {
            Completed("release", release.Operation, new { unverified = "VersionNumber could not be read." }),
            Completed("edit", edit.Operation, new { unverified = (string?)null }),
            // Superseded by "again" on the same target: the later item covers it, its own evidence does not matter.
            Completed("place", place.Operation, new { unverified = "CreateFrom is bound without a return value." }),
            Completed("again", replace.Operation, new { }),
        });

        var verification = (await domain.VerifyAsync(batch))!;

        Assert.False(verification.Success);
        Assert.Equal(new[] { ("release", "notVerified", false), ("edit", "trusted", true), ("place", "superseded", true), ("again", "trusted", true) },
            verification.Operations.Select(entry => (entry.OperationId, entry.Check, entry.Success)));
        Assert.Contains("VersionNumber could not be read.", verification.Operations[0].Message, StringComparison.Ordinal);
        Assert.False(domain.VerificationSucceeded(verification));
    }

    /// <summary>
    /// K47 (live 2026-11-02): in one call copy_master_copy (project -> global library) was reported superseded by a later
    /// delete_master_copy of the same name in the project library, and save_global_library by close_global_library.
    /// </summary>
    [Fact]
    public async Task Verification_KeysTheCopyByItsTargetLibrary_AndCloseDoesNotSupersedeSave()
    {
        StructuredOperationItem Completed(LibraryOperationRequest item)
            => new(item.OperationId, item.Operation, OperationBatchStatus.Succeeded, CanonicalJson.ToElement(new { }), null, null, null, Array.Empty<string>());
        var copy = new LibraryOperationRequest { OperationId = "copy", Operation = LibraryMethods.CopyMasterCopy, Library = "project", MasterCopyName = "Motor", TargetLibrary = Plant };
        var delete = new LibraryOperationRequest { OperationId = "delete", Operation = LibraryMethods.DeleteMasterCopy, Library = "project", MasterCopyName = "Motor" };
        var save = Item("save", LibraryMethods.SaveGlobalLibrary, Plant);
        var close = Item("close", LibraryMethods.CloseGlobalLibrary, Plant);
        var items = new[] { copy, delete, save, close };
        var worker = new Worker();
        worker.MasterCopies[Plant] = new List<string> { "Motor" };
        var domain = new LibraryWriteDomain(worker.Call);
        domain.Validate(items, McpAccessMode.Full);

        var verification = (await domain.VerifyAsync(StructuredOperationBatch.FromItems(items.Select(Completed).ToList())))!;

        // K61: the copy, the delete and the close are read back; the save stays trusted and says why.
        Assert.Equal(new[] { ("copy", "masterCopy", true), ("delete", "masterCopy", true), ("save", "trusted", true), ("close", "openLibraries", true) },
            verification.Operations.Select(entry => (entry.OperationId, entry.Check, entry.Success)));
        Assert.Contains("no documented Openness member reports whether a global library has unsaved changes", verification.Operations[2].Message);
        // Without the save, the close still supersedes the copy (it may discard it), and so does a later delete of the copy.
        var policy = new LibraryVerificationPolicy(new[] { copy, delete, close });
        var own = policy.ObjectKeys(copy, asLater: false);
        var deleteCopy = new LibraryOperationRequest { OperationId = "d2", Operation = LibraryMethods.DeleteMasterCopy, Library = Plant, MasterCopyName = "Motor" };
        Assert.Contains(policy.ObjectKeys(deleteCopy, asLater: true), key => own.Contains(key));
        Assert.Contains(own, key => key.StartsWith(policy.ObjectKeys(close, asLater: true).Single() + "\n", StringComparison.Ordinal));
        Assert.DoesNotContain(policy.ObjectKeys(save, asLater: false), key => key.StartsWith(policy.ObjectKeys(close, asLater: true).Single(), StringComparison.Ordinal));
        // Saved before the close: only a later content item on the same object supersedes it.
        var saved = new LibraryVerificationPolicy(items).ObjectKeys(copy, asLater: false);
        Assert.DoesNotContain(saved, key => key.StartsWith(policy.ObjectKeys(close, asLater: true).Single() + "\n", StringComparison.Ordinal));
        Assert.Contains(policy.ObjectKeys(deleteCopy, asLater: true), key => saved.Contains(key));
    }

    /// <summary>
    /// K61 (live V21 2026-11-02): create_global_library, the master copy items, open and close said "trusted, nothing was
    /// read back" although library_read reads their result. They are read back with list_libraries and get_library_tree.
    /// </summary>
    [Fact]
    public async Task Verification_ReadsLifecycleAndMasterCopyItemsBack()
    {
        const string Created = @"C:\Libraries\New\Lib\Lib.al21";
        const string Other = @"C:\Other\Other.al21";
        StructuredOperationItem Completed(LibraryOperationRequest item, object result)
            => new(item.OperationId, item.Operation, OperationBatchStatus.Succeeded, CanonicalJson.ToElement(result), null, null, null, Array.Empty<string>());
        var create = new LibraryOperationRequest { OperationId = "create", Operation = LibraryMethods.CreateGlobalLibrary, TargetDirectory = @"C:\Libraries\New", Name = "Lib" };
        var open = Item("open", LibraryMethods.OpenGlobalLibrary, path: Plant);
        var capture = new LibraryOperationRequest
        {
            OperationId = "capture", Operation = LibraryMethods.CreateMasterCopy, Library = "project",
            Source = new() { Kind = LibraryPolicy.SourceKinds.Block, PlcName = "PLC_1", Path = "Main" },
        };
        var close = Item("close", LibraryMethods.CloseGlobalLibrary, Other);
        var items = new[] { create, open, capture, close };
        var worker = new Worker();
        worker.OpenLibraries.AddRange(new[] { Created, Other });
        var domain = new LibraryWriteDomain(worker.Call);
        domain.Validate(items, McpAccessMode.Full);

        var verification = (await domain.VerifyAsync(StructuredOperationBatch.FromItems(new List<StructuredOperationItem>
        {
            Completed(create, new { library = new { library = Created, kind = "user", path = Created } }),
            Completed(open, new { library = new { library = Plant, kind = "user", path = Plant } }),
            Completed(capture, new { created = new[] { new { name = "Main" } } }),
            Completed(close, new { }),
        })))!;

        Assert.Equal(new[]
            {
                ("create", "openLibraries", true, $"{Created} open"), ("open", "openLibraries", false, $"{Plant} not open"),
                ("capture", "masterCopy", false, "project: master copy 'Main' absent"), ("close", "openLibraries", false, $"{Other} open"),
            },
            verification.Operations.Select(entry => (entry.OperationId, entry.Check, entry.Success, entry.Actual!)));
        Assert.False(verification.Success);
        Assert.Contains(worker.Calls, call => call.Method == LibraryMethods.ForOperation(LibraryMethods.GetLibraryTree)
            && call.Request.Library == "project" && call.Request.Content == LibraryPolicy.TreeContents.MasterCopies);
    }

    [Fact]
    public void Verification_SupersedesContentByALaterCloseOfTheSameLibrary_NotBySave()
    {
        var policy = new LibraryVerificationPolicy();
        var rename = new LibraryOperationRequest { OperationId = "r", Operation = LibraryMethods.RenameType, Library = Plant, TypePath = "/A" };
        var save = Item("s", LibraryMethods.SaveGlobalLibrary, Plant);
        var close = Item("c", LibraryMethods.CloseGlobalLibrary, Plant.ToLowerInvariant());
        var own = policy.ObjectKeys(rename, asLater: false).Single();
        Assert.Empty(policy.ObjectKeys(save, asLater: true));
        Assert.StartsWith(policy.ObjectKeys(close, asLater: true).Single() + "\n", own);
        Assert.False(policy.HasReadBack(rename));
    }
}
