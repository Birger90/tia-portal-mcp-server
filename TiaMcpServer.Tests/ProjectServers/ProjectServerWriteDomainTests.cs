using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Multiuser;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.ProjectServers;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.ProjectServers;

/// <summary>project_server_write validation, planning from the inventory, guards, prompt and read-back through the worker seam.</summary>
public sealed class ProjectServerWriteDomainTests
{
    private static readonly ProjectBindingSnapshot Unbound = new(ProjectBindingSnapshot.UnboundState, "binding", 0,
        projectPath: null, workerSessionId: null, sessionGeneration: null, portalProcessId: null, invalidatedReason: null);

    private static readonly string SessionPath = Path.Combine(Path.GetTempPath(), "psrv-unit", "LineA_1", "LineA_1.als21");

    /// <summary>An in-memory inventory: one connection PsrvA, root project LineA with session 1, group Plant with LineB.</summary>
    private sealed class Inventory
    {
        public Dictionary<string, (string Host, int Port)> Connections { get; } = new(StringComparer.Ordinal) { ["PsrvA"] = ("mu-a.example", 8735) };
        public List<string> Root { get; } = ["LineA"];
        public List<int> Sessions { get; } = [1];
        public List<string> Reads { get; } = new();
        public List<(string Method, ProjectServerRequestInfo Request)> Writes { get; } = new();

        public Task<WorkerCallResult> Inspect(WorkerRequest request)
        {
            Reads.Add(request.Method);
            var observation = new ProjectServerConnectionObservation { State = "connected", ObservationSource = "explicitRead", ObservedAt = DateTimeOffset.UtcNow };
            MultiuserRemoteIdentity Identity() => new()
            {
                ServerAlias = request.MultiuserServerAlias!, Host = Connections[request.MultiuserServerAlias!].Host, Port = Connections[request.MultiuserServerAlias!].Port,
                Group = request.MultiuserGroupIsRoot is { } root ? new() { IsRoot = root, Name = request.MultiuserGroupName } : null,
                ServerProjectName = request.MultiuserServerProjectName,
            };
            object payload = request.Method switch
            {
                "list_server_connections" => new MultiuserServerConnectionsInfo
                    { Connections = Connections.Select(c => new MultiuserServerConnectionInfo { ServerAlias = c.Key, Host = c.Value.Host, Port = c.Value.Port }).ToList() },
                "list_server_groups" => new MultiuserServerGroupsInfo { RemoteIdentity = Identity(), ConnectionObservation = observation, Groups = [new() { IsRoot = false, Name = "Plant" }] },
                "list_server_projects" => new MultiuserServerProjectsInfo
                {
                    RemoteIdentity = Identity(), ConnectionObservation = observation,
                    Projects = (request.MultiuserGroupIsRoot == true ? Root : ["LineB"]).Select(n => new MultiuserServerProjectInfo { Name = n }).ToList(),
                },
                "list_local_sessions" => new MultiuserLocalSessionsInfo
                {
                    RemoteIdentity = Identity(), ConnectionObservation = observation,
                    Sessions = Sessions.Select(id => new MultiuserLocalSessionInfo { SessionId = id, ProjectPath = id == 1 ? SessionPath : SessionPath + id }).ToList(),
                },
                _ => throw new InvalidOperationException(request.Method),
            };
            return Task.FromResult(new WorkerCallResult(true, WorkerJson.SerializePayload(payload), null, null, Array.Empty<string>()) { PortalProcessId = 4242 });
        }

        public Task<WorkerCallResult> Write(string method, ProjectServerRequestInfo request)
        {
            Writes.Add((method, request));
            var result = new ProjectServerWriteResultInfo { Operation = request.Operation, ServerAlias = request.ServerAlias!, PortalConfirmationPolicy = request.PortalConfirmations };
            switch (request.Operation)
            {
                case ProjectServerMethods.CreateServerConnection: Connections[request.ServerAlias!] = (request.Host!, request.Port!.Value); break;
                case ProjectServerMethods.SetServerConnection:
                    var current = Connections[request.ServerAlias!];
                    Connections[request.ServerAlias!] = (request.Host ?? current.Host, request.Port ?? current.Port);
                    break;
                case ProjectServerMethods.DeleteServerConnection: Connections.Remove(request.ServerAlias!); break;
                case ProjectServerMethods.AddProjectToServer: Root.Add(Path.GetFileNameWithoutExtension(request.ProjectPath)!); break;
                case ProjectServerMethods.CreateLocalSession: Sessions.Add(2); result.SessionId = 2; result.SessionPath = SessionPath + 2; break;
                case ProjectServerMethods.DeleteLocalSessionFromServer: Sessions.Remove(request.SessionId!.Value); result.SessionId = request.SessionId; break;
            }

            return Task.FromResult(new WorkerCallResult(true, WorkerJson.SerializePayload(result), null, null, Array.Empty<string>()));
        }
    }

    private static ProjectServerWriteDomain Domain(Inventory inventory) => new(inventory.Inspect, inventory.Write, () => Unbound);

    private static MultiuserGroupSelector Root => new() { IsRoot = true, Name = null };

    private static ProjectServerOperationRequest Item(string id, string operation) => new() { OperationId = id, Operation = operation, ServerAlias = "PsrvA" };

    [Fact]
    public void Validate_ReadOnly_IsAccessDenied()
    {
        var validation = Domain(new()).Validate([Item("d", ProjectServerMethods.DeleteServerConnection)], McpAccessMode.ReadOnly);
        Assert.Equal(WorkerFailureCategories.AccessDenied, validation.Error?.Category);
    }

    [Theory]
    [InlineData(ProjectServerMethods.DeleteServerConnection, "host")]
    [InlineData(ProjectServerMethods.CreateServerConnection, "missing")]
    [InlineData(ProjectServerMethods.SetServerConnection, "none")]
    [InlineData(ProjectServerMethods.DeleteLocalSessionFromServer, "group")]
    [InlineData("rename_server", "unknown")]
    public void Validate_RefusesForeignMissingOrUnknownFields(string operation, string variant)
    {
        var item = Item("x", operation);
        if (variant == "host") item.Host = "h";
        if (variant == "group") { item.ServerProjectName = "LineA"; item.SessionId = 1; }
        var validation = Domain(new()).Validate([item], McpAccessMode.ReadWrite);
        Assert.False(validation.IsValid);
        Assert.Equal(WorkerFailureCategories.ValidationError, validation.Error!.Category);
    }

    [Fact]
    public void Validate_RefusesProtocolPortAndUploadOfNonProject()
    {
        var bad = Item("c", ProjectServerMethods.CreateServerConnection);
        bad.Host = "h"; bad.Port = 70000; bad.Protocol = "Https";
        Assert.False(Domain(new()).Validate([bad], McpAccessMode.Full).IsValid);
        bad.Port = 80; bad.Protocol = "ftp";
        Assert.False(Domain(new()).Validate([bad], McpAccessMode.Full).IsValid);
        var upload = Item("u", ProjectServerMethods.AddProjectToServer);
        upload.Group = Root; upload.ProjectPath = Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".ap21");
        Assert.Contains("existing", Domain(new()).Validate([upload], McpAccessMode.Full).Error!.Message);
    }

    [Fact]
    public async Task Plan_DeleteSessionAndConnection_FireAcknowledgeGuards_CreateAndSetAreInfo()
    {
        var inventory = new Inventory();
        var domain = Domain(inventory);
        var delete = Item("del", ProjectServerMethods.DeleteLocalSessionFromServer);
        delete.Group = Root; delete.ServerProjectName = "LineA"; delete.SessionId = 1;
        var set = Item("set", ProjectServerMethods.SetServerConnection);
        set.Port = 9000;
        var create = Item("new", ProjectServerMethods.CreateServerConnection);
        create.ServerAlias = "PsrvB"; create.Host = "mu-b"; create.Port = 443; create.Protocol = "Https";
        var dropB = Item("drop", ProjectServerMethods.DeleteServerConnection);
        dropB.ServerAlias = "PsrvB";
        var items = new[] { delete, set, create, dropB };
        Assert.True(domain.Validate(items, McpAccessMode.ReadWrite).IsValid);

        var plan = await domain.PlanAsync(items);

        Assert.True(plan.Success, plan.Error?.Message);
        var guards = domain.EvaluateGuards(items, plan.Items);
        var catalog = new WriteGuardCatalog(ProjectServerGuardDefinitions.Definitions);
        var acknowledged = guards.Where(g => catalog.Get(g.Id).Severity == WriteGuardSeverities.Acknowledge).Select(g => (g.Id, g.OperationId)).ToArray();
        Assert.Equal(new[] { (ProjectServerGuardDefinitions.DeletesLocalSession, (string?)"del"), (ProjectServerGuardDefinitions.DeletesConnection, "drop") }, acknowledged);
        Assert.Contains(guards, g => g.Id == ProjectServerGuardDefinitions.ChangesConnection && g.Message.Contains("8735 -> 9000", StringComparison.Ordinal));
        Assert.Empty(inventory.Writes);
        var decision = GuardDecisions.Decide(guards, ConfirmationMode.AskUser, catalog, dryRun: false);
        Assert.Equal(GuardDecisionKind.NeedsUser, decision.Kind);
        Assert.Equal(GuardDecisionKind.Proceed, GuardDecisions.Decide(guards, ConfirmationMode.Policy, catalog, dryRun: false).Kind);
    }

    [Fact]
    public async Task Plan_UnknownAliasOrSession_RefusesTheWholeCall()
    {
        var domain = Domain(new());
        var missing = Item("missing", ProjectServerMethods.DeleteLocalSessionFromServer);
        missing.Group = Root; missing.ServerProjectName = "LineA"; missing.SessionId = 7;
        var plan = await domain.PlanAsync([missing]);
        Assert.False(plan.Success);
        Assert.Equal(WorkerFailureCategories.TargetNotFound, plan.Error!.Category);
        Assert.Contains("No item of this call ran", plan.Error.Message, StringComparison.Ordinal);

        var unknown = Item("u", ProjectServerMethods.DeleteServerConnection);
        unknown.ServerAlias = "Customer";
        Assert.Equal(WorkerFailureCategories.TargetNotFound, (await domain.PlanAsync([unknown])).Error!.Category);
    }

    [Fact]
    public async Task Plan_ItemOnAConnectionCreatedEarlier_IsResolvedAtMutation()
    {
        var inventory = new Inventory();
        var domain = Domain(inventory);
        var create = Item("new", ProjectServerMethods.CreateServerConnection);
        create.ServerAlias = "PsrvB"; create.Host = "mu-b"; create.Port = 443; create.Protocol = "Https";
        var session = Item("session", ProjectServerMethods.CreateLocalSession);
        session.ServerAlias = "PsrvB"; session.Group = Root; session.ServerProjectName = "X"; session.SessionName = "S"; session.SessionDirectory = Path.GetTempPath();
        var plan = await domain.PlanAsync([create, session]);
        Assert.True(plan.Success, plan.Error?.Message);
        Assert.Contains(domain.EvaluateGuards([create, session], plan.Items), g => g.Id == ProjectServerGuardDefinitions.ResolvedLater && g.OperationId == "session");
        Assert.Equal(new[] { "list_server_connections" }, inventory.Reads);
    }

    [Fact]
    public async Task Confirmation_QuotesEveryTargetAndOperationId()
    {
        var domain = Domain(new());
        var delete = Item("del-1", ProjectServerMethods.DeleteServerConnection);
        var plan = await domain.PlanAsync([delete]);
        var prompt = domain.DescribeForConfirmation([delete], plan.Items);
        Assert.Contains("\"connection 'PsrvA'\"", prompt, StringComparison.Ordinal);
        Assert.Contains("operationId \"del-1\"", prompt, StringComparison.Ordinal);
        Assert.Contains("TIA Portal dialogs: decline", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verification_ReadsBackThroughTheInventory()
    {
        var inventory = new Inventory();
        var domain = Domain(inventory);
        var set = Item("set", ProjectServerMethods.SetServerConnection);
        set.Host = "mu-a2.example"; set.Protocol = "Http";
        var session = Item("session", ProjectServerMethods.CreateLocalSession);
        session.Group = Root; session.ServerProjectName = "LineA"; session.SessionName = "S"; session.SessionDirectory = Path.GetTempPath();
        var delete = Item("del", ProjectServerMethods.DeleteLocalSessionFromServer);
        delete.Group = Root; delete.ServerProjectName = "LineA"; delete.SessionId = 1;
        var items = new[] { set, session, delete };
        Assert.True(domain.Validate(items, McpAccessMode.Full).IsValid);
        Assert.True((await domain.PlanAsync(items)).Success);
        var outcomes = new List<StructuredOperationItem>();
        foreach (var item in items) outcomes.Add(domain.Project(item, await domain.MutateAsync(item)));
        Assert.All(outcomes, o => Assert.Equal(OperationBatchStatus.Succeeded, o.Status));

        var verification = await domain.VerifyAsync(StructuredOperationBatch.FromItems(outcomes));

        Assert.NotNull(verification);
        Assert.True(verification!.Success, string.Join("\n", verification.Operations.Select(o => o.Message)));
        Assert.Equal(new[] { "serverConnection", "localSession", "localSession" }, verification.Operations.Select(o => o.Check));
        Assert.Contains("protocol has no documented read", verification.Operations[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Projection_DeclinedPortalConfirmation_FailsTheItem()
    {
        var domain = Domain(new());
        var delete = Item("d", ProjectServerMethods.DeleteServerConnection);
        var result = new ProjectServerWriteResultInfo
        {
            Operation = delete.Operation, ServerAlias = "PsrvA", PortalConfirmationPolicy = "decline", PortalConfirmationsObserved = 1,
            PortalConfirmations = [new() { Type = "Confirmation", Text = "Delete?", Answer = "No" }],
        };
        await Task.CompletedTask;
        var projected = domain.Project(delete, new WorkerCallResult(true, WorkerJson.SerializePayload(result), null, null, Array.Empty<string>()));
        Assert.Equal(OperationBatchStatus.Failed, projected.Status);
        Assert.NotNull(projected.Result);
        Assert.Contains("portalConfirmations: \"accept\"", projected.Failure!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_PolicyAndCapability_ArePortalLevelAndNotReadOnly()
    {
        foreach (var method in ProjectServerMethods.WorkerMethods)
        {
            Assert.Equal(OperationCapability.ProjectLifecycle, OperationPolicyCatalog.GetCapability(method));
            Assert.False(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, method));
            Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadWrite, method));
            Assert.False(OperationPolicyCatalog.RequiresExpectedSessionIdentity(method));
            Assert.Null(ProjectCapabilityCatalog.OperationFor(method));
            Assert.False(ProjectCapabilityCatalog.RequiresBoundProject(method));
        }

        Assert.True(ProjectServerMethods.IsAcknowledged(ProjectServerMethods.AddProjectToServer));
        Assert.False(ProjectServerMethods.IsAcknowledged(ProjectServerMethods.CreateLocalSession));
    }
}
