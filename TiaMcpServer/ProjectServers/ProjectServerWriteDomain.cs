using System.Text.Json;
using TiaMcpServer.Contracts.Multiuser;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.PlcOnline;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;

namespace TiaMcpServer.ProjectServers;

/// <summary>
/// project_server_write on the guarded single-call pipeline (owner decision 2026-11-09; not live-tested): the Project Server
/// (Multiuser) administration of one TIA Portal instance. Portal-scoped (<see cref="IsPortalScoped"/>): no binding is needed;
/// a verified binding pins the TIA Portal the call acts in. Planning reads only the existing bind_project inventory
/// (list_server_connections, list_server_groups, list_server_projects, list_local_sessions) and simulates the call's own
/// connection changes, so a later item may use a connection an earlier item creates (it is then resolved only when it runs).
/// Deleting a local session, deleting a connection and uploading a project fire acknowledge guards (read-write asks the human
/// for the call, full satisfies them by policy); the other items never prompt. Mutations stop at the first failure without
/// rollback; every item is read back through the same inventory reads where a documented read exists
/// (<see cref="ProjectServerVerificationPolicy"/>). The tool never selects a server: the caller names the connection.
/// </summary>
public sealed class ProjectServerWriteDomain
    : IWriteDomain<ProjectServerOperationRequest, ProjectServerWriteEffect, WriteVerification, ProjectServerGuardedWriteResponse>
{
    public const string ContractVersionValue = "1.0";

    private readonly Func<WorkerRequest, Task<WorkerCallResult>> _inspect;
    private readonly Func<string, ProjectServerRequestInfo, Task<WorkerCallResult>> _write;
    private readonly Func<ProjectBindingSnapshot> _binding;
    private readonly int? _portalProcessId;
    private readonly Dictionary<string, IReadOnlyList<FiredGuard>> _guards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PlannedState> _planned = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProjectServerWriteResultInfo> _results = new(StringComparer.Ordinal);
    private readonly List<string> _planWarnings = new();
    private IReadOnlyList<ProjectServerOperationRequest> _items = [];

    public ProjectServerWriteDomain(OpennessWorkerClient client, int? portalProcessId = null)
        : this(async request => (await client.InspectPortalAsync(request).ConfigureAwait(false)).Result,
            (method, info) => client.ProjectServerAsync(method, info, portalProcessId), () => client.BindingSnapshot, portalProcessId)
    {
    }

    /// <summary>Worker-independent seam for unit tests.</summary>
    internal ProjectServerWriteDomain(Func<WorkerRequest, Task<WorkerCallResult>> inspect,
        Func<string, ProjectServerRequestInfo, Task<WorkerCallResult>> write, Func<ProjectBindingSnapshot> binding, int? portalProcessId = null)
    {
        _inspect = inspect ?? throw new ArgumentNullException(nameof(inspect));
        _write = write ?? throw new ArgumentNullException(nameof(write));
        _binding = binding ?? throw new ArgumentNullException(nameof(binding));
        _portalProcessId = portalProcessId;
    }

    /// <summary>What planning observed for one item, kept for its read-back.</summary>
    internal sealed record PlannedState(IReadOnlyList<string>? ProjectsBefore, IReadOnlyList<int>? SessionsBefore, bool ResolvedLater);

    public string ToolName => ProjectServerMethods.WriteTool;

    public string ContractVersion => ContractVersionValue;

    public bool IsPortalScoped => true;

    internal IReadOnlyDictionary<string, PlannedState> Planned => _planned;

    internal IReadOnlyDictionary<string, ProjectServerWriteResultInfo> Results => _results;

    public string DescribeForConfirmation(IReadOnlyList<ProjectServerOperationRequest> items, IReadOnlyList<ItemPlan<ProjectServerWriteEffect>> plans)
    {
        var portal = _binding() is { IsVerified: true } binding ? $"the TIA Portal of the bound project (process {binding.PortalProcessId})"
            : _portalProcessId is { } pid ? $"TIA Portal process {pid}" : "the TIA Portal this server attaches to";
        var lines = items.Select((item, index) =>
        {
            var effect = index < plans.Count ? plans[index]?.Effect : null;
            return $"{index + 1}. {item.Operation} {ConfirmationText.Quote(effect?.Target ?? item.ServerAlias)}; TIA Portal dialogs: "
                + $"{Policy(item)}; operationId {ConfirmationText.Quote(item.OperationId)}";
        });
        return $"Confirm project_server_write in {portal}: this call changes Project Server connections of this TIA Portal instance "
            + "or content on a Project Server (not the bound project). The caller chose these servers:\n" + string.Join("\n", lines)
            + "\nItems run in order and stop at the first failure without rollback.";
    }

    // ============================================================== validation

    public WriteValidation Validate(IReadOnlyList<ProjectServerOperationRequest> items, McpAccessMode accessMode)
    {
        _items = items.ToArray();
        if (accessMode == McpAccessMode.ReadOnly)
            return WriteValidation.Invalid(WorkerFailureCategories.AccessDenied, "project_server_write requires read-write or full access mode.");
        if (_portalProcessId is <= 0) return Invalid("portalProcessId must be positive.");
        foreach (var item in items)
        {
            if (ValidateItem(item) is { } error) return Invalid($"Operation '{item.OperationId}': {error}");
        }

        if (ProjectServerWritePayloadBudget.MeasureProtectedCore(items) > StructuredOperationBatchPayloadBudget.MaxDocumentChars)
            return Invalid("The encoded operation identities leave insufficient room for the required response summaries. Use smaller project_server_write calls.");
        return WriteValidation.Valid();
    }

    private static readonly IReadOnlyDictionary<string, string[]> AllowedFields = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        [ProjectServerMethods.CreateServerConnection] = ["serverAlias", "host", "port", "protocol"],
        [ProjectServerMethods.SetServerConnection] = ["serverAlias", "host", "port", "protocol"],
        [ProjectServerMethods.DeleteServerConnection] = ["serverAlias"],
        [ProjectServerMethods.AddProjectToServer] = ["serverAlias", "group", "projectPath"],
        [ProjectServerMethods.CreateLocalSession] = ["serverAlias", "group", "serverProjectName", "sessionName", "sessionDirectory", "sessionMode"],
        [ProjectServerMethods.DeleteLocalSessionFromServer] = ["serverAlias", "group", "serverProjectName", "sessionId"],
    };

    internal static string? ValidateItem(ProjectServerOperationRequest item)
    {
        if (!AllowedFields.TryGetValue(item.Operation ?? string.Empty, out var allowed))
            return $"unknown operation '{item.Operation}'. Operations: {string.Join(", ", ProjectServerMethods.Operations)}.";
        var present = new (string Name, bool Set)[]
        {
            ("serverAlias", item.ServerAlias is not null), ("host", item.Host is not null), ("port", item.Port is not null),
            ("protocol", item.Protocol is not null), ("group", item.Group is not null), ("serverProjectName", item.ServerProjectName is not null),
            ("projectPath", item.ProjectPath is not null), ("sessionName", item.SessionName is not null),
            ("sessionDirectory", item.SessionDirectory is not null), ("sessionMode", item.SessionMode is not null), ("sessionId", item.SessionId is not null),
        };
        var foreign = present.Where(field => field.Set && !allowed.Contains(field.Name)).Select(field => field.Name).ToArray();
        if (foreign.Length > 0) return $"{item.Operation} does not take {string.Join(", ", foreign)}.";
        if (item.PortalConfirmations is not null && !PlcDownloadPolicy.PortalConfirmationPolicies.IsKnown(item.PortalConfirmations))
            return $"portalConfirmations must be one of: {string.Join(", ", PlcDownloadPolicy.PortalConfirmationPolicies.All)}.";
        if (string.IsNullOrWhiteSpace(item.ServerAlias)) return "serverAlias is required (an exact alias).";
        if (item.Host is not null && string.IsNullOrWhiteSpace(item.Host)) return "host must not be blank.";
        if (item.Port is { } port && port is <= 0 or > 65535) return "port must be between 1 and 65535.";
        if (item.Protocol is not null && !ProjectServerMethods.Protocols.Contains(item.Protocol, StringComparer.Ordinal))
            return $"protocol must be one of: {string.Join(", ", ProjectServerMethods.Protocols)}.";
        switch (item.Operation)
        {
            case ProjectServerMethods.CreateServerConnection:
                if (item.Host is null || item.Port is null || item.Protocol is null) return "create_server_connection requires host, port and protocol.";
                break;
            case ProjectServerMethods.SetServerConnection:
                if (item.Host is null && item.Port is null && item.Protocol is null) return "set_server_connection requires host, port or protocol.";
                break;
            case ProjectServerMethods.AddProjectToServer:
                if (GroupError(item) is { } addGroup) return addGroup;
                if (PlcFileTargets.PathShapeError("projectPath", item.ProjectPath) is { } shape) return shape;
                if (!Path.IsPathFullyQualified(item.ProjectPath!)) return "projectPath must be an absolute path.";
                if (!string.Equals(Path.GetExtension(item.ProjectPath), ".ap21", StringComparison.OrdinalIgnoreCase))
                    return "projectPath must be a single-user project file (.ap21).";
                if (!File.Exists(item.ProjectPath)) return "projectPath must be an existing project file.";
                break;
            case ProjectServerMethods.CreateLocalSession:
                if (GroupError(item) is { } createGroup) return createGroup;
                if (string.IsNullOrWhiteSpace(item.ServerProjectName)) return "serverProjectName is required.";
                if (string.IsNullOrWhiteSpace(item.SessionName)) return "sessionName is required.";
                if (item.SessionName!.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || item.SessionName.IndexOfAny(['\\', '/', ':']) >= 0)
                    return "sessionName must be a valid file name.";
                if (PlcFileTargets.PathShapeError("sessionDirectory", item.SessionDirectory) is { } dir) return dir;
                if (!Path.IsPathFullyQualified(item.SessionDirectory!)) return "sessionDirectory must be an absolute directory.";
                if (item.SessionMode is not null && !ProjectServerMethods.SessionCreationModes.Contains(item.SessionMode, StringComparer.Ordinal))
                    return $"sessionMode must be one of: {string.Join(", ", ProjectServerMethods.SessionCreationModes)}.";
                break;
            case ProjectServerMethods.DeleteLocalSessionFromServer:
                if (GroupError(item) is { } deleteGroup) return deleteGroup;
                if (string.IsNullOrWhiteSpace(item.ServerProjectName)) return "serverProjectName is required.";
                if (item.SessionId is null) return "sessionId is required (bind_project list_local_sessions).";
                break;
        }

        return null;
    }

    private static string? GroupError(ProjectServerOperationRequest item)
        => item.Group?.IsRoot is not { } root ? "group is required: {isRoot:true,name:null} or {isRoot:false,name}."
            : root ? item.Group.Name is null ? null : "the root group requires name:null."
            : string.IsNullOrWhiteSpace(item.Group.Name) ? "a named group requires a nonblank name." : null;

    // ============================================================== planning

    public async Task<WritePlan<ProjectServerWriteEffect>> PlanAsync(IReadOnlyList<ProjectServerOperationRequest> items)
    {
        _items = items.ToArray();
        _guards.Clear();
        _planned.Clear();
        var connections = await ReadAsync<MultiuserServerConnectionsInfo>(Inventory("list_server_connections", null, null, null),
            info => info.ServerConnections).ConfigureAwait(false);
        if (connections.Error is { } connectionError) return WritePlan<ProjectServerWriteEffect>.Fail(connectionError.Category, connectionError.Message);
        var simulated = connections.Value!.Connections.ToDictionary(row => row.ServerAlias, row => (row.Host, (int?)row.Port), StringComparer.Ordinal);
        var touched = new HashSet<string>(StringComparer.Ordinal);
        var bound = _binding() is { IsVerified: true } binding ? ProjectPathNormalization.Canonicalize(binding.ProjectPath) : null;
        var plans = new List<ItemPlan<ProjectServerWriteEffect>>();
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var guards = new List<FiredGuard>();
            void Fire(string id, string message) => guards.Add(new(id, item.OperationId, message));
            var alias = item.ServerAlias!;
            var consequences = new List<string>();
            string target;
            ProjectServerWriteEffect? effect = null;
            WriteToolError? failure = null;
            var exists = simulated.TryGetValue(alias, out var endpoint);
            if (item.Operation != ProjectServerMethods.CreateServerConnection && !exists)
            {
                failure = new(WorkerFailureCategories.TargetNotFound,
                    $"No Project Server connection '{alias}' is listed by bind_project list_server_connections" + (touched.Count > 0 ? " (after the earlier items of this call)." : "."));
            }

            switch (item.Operation)
            {
                case ProjectServerMethods.CreateServerConnection:
                    target = $"connection '{alias}' to {item.Protocol}://{item.Host}:{item.Port}";
                    consequences.Add($"Adds the Project Server connection '{alias}' ({item.Protocol}, {item.Host}, port {item.Port}) to this TIA Portal instance.");
                    Fire(ProjectServerGuardDefinitions.CreatesConnection, $"Adds the connection '{alias}' to {item.Host}:{item.Port}.");
                    if (exists) Fire(ProjectServerGuardDefinitions.AliasExists, $"A connection '{alias}' is already listed ({endpoint.Host}:{endpoint.Item2}); TIA Portal decides.");
                    simulated[alias] = (item.Host!, item.Port);
                    touched.Add(alias);
                    break;
                case ProjectServerMethods.SetServerConnection:
                    target = $"connection '{alias}'";
                    if (failure is null)
                    {
                        if (item.Protocol is not null) consequences.Add($"Protocol set to {item.Protocol} (SetProtocol; no documented read shows the protocol).");
                        if (item.Host is not null) consequences.Add($"Host {endpoint.Host} -> {item.Host} (SetHostName).");
                        if (item.Port is not null) consequences.Add($"Port {endpoint.Item2} -> {item.Port} (SetPort).");
                        Fire(ProjectServerGuardDefinitions.ChangesConnection, $"Changes the connection '{alias}': {string.Join(" ", consequences)}");
                        simulated[alias] = (item.Host ?? endpoint.Host, item.Port ?? endpoint.Item2);
                        touched.Add(alias);
                    }

                    break;
                case ProjectServerMethods.DeleteServerConnection:
                    target = $"connection '{alias}'";
                    if (failure is null)
                    {
                        consequences.Add($"Deletes the connection '{alias}' ({endpoint.Host}:{endpoint.Item2}) from this TIA Portal instance; the server and its projects are not deleted.");
                        Fire(ProjectServerGuardDefinitions.DeletesConnection, $"Deletes the Project Server connection '{alias}' ({endpoint.Host}:{endpoint.Item2}) of this TIA Portal instance.");
                        simulated.Remove(alias);
                        touched.Add(alias);
                    }

                    break;
                default:
                    target = $"{GroupText(item)} on '{alias}'" + (item.ServerProjectName is null ? string.Empty : $", project '{item.ServerProjectName}'");
                    if (failure is null)
                    {
                        var resolvedLater = touched.Contains(alias);
                        var planned = resolvedLater ? new PlannedState(null, null, true)
                            : await PlanServerItemAsync(item, items.Take(index).ToArray(), bound, consequences, Fire).ConfigureAwait(false);
                        if (planned is null) failure = _lastFailure;
                        else
                        {
                            _planned[item.OperationId] = planned;
                            if (planned.ResolvedLater)
                                Fire(ProjectServerGuardDefinitions.ResolvedLater, $"'{alias}' is created or changed by an earlier item of this call (or the target is), so the target is resolved when the item runs.");
                        }

                        if (failure is null) FireServerItemGuards(item, Fire);
                        if (item.Operation == ProjectServerMethods.AddProjectToServer) target = $"upload of {item.ProjectPath} to {target}";
                        if (item.Operation == ProjectServerMethods.CreateLocalSession) target += $": new session '{item.SessionName}' in {item.SessionDirectory} ({item.SessionMode ?? ProjectServerMethods.SessionCreationModes[0]})";
                        if (item.Operation == ProjectServerMethods.DeleteLocalSessionFromServer) target += $": session {item.SessionId}";
                    }

                    break;
            }

            if (failure is not null)
                return WritePlan<ProjectServerWriteEffect>.Fail(failure.Category, $"Operation '{item.OperationId}': {failure.Message} No item of this call ran.");
            consequences.Add(Policy(item) == PlcDownloadPolicy.PortalConfirmationPolicies.Accept
                ? "TIA Portal confirmation dialogs raised by this item are answered Yes and recorded (portalConfirmations: accept)."
                : "TIA Portal confirmation dialogs raised by this item are answered No, recorded and fail the item (portalConfirmations: decline).");
            effect = new ProjectServerWriteEffect(item.Operation, target, consequences.ToArray());
            _guards[item.OperationId] = guards;
            plans.Add(ItemPlan<ProjectServerWriteEffect>.Resolved(effect));
        }

        return WritePlan<ProjectServerWriteEffect>.Ok(plans);
    }

    private WriteToolError? _lastFailure;

    /// <summary>Resolves the group, project and session of one server item against the live inventory; null with <see cref="_lastFailure"/> when it cannot.</summary>
    private async Task<PlannedState?> PlanServerItemAsync(ProjectServerOperationRequest item, IReadOnlyList<ProjectServerOperationRequest> earlier,
        string? boundPath, List<string> consequences, Action<string, string> fire)
    {
        _lastFailure = null;
        var alias = item.ServerAlias!;
        if (item.Group!.IsRoot == false)
        {
            var groups = await ReadAsync<MultiuserServerGroupsInfo>(Inventory("list_server_groups", alias, null, null), info => info.ServerGroups).ConfigureAwait(false);
            if (groups.Error is not null) return Fail(groups.Error);
            if (!groups.Value!.Groups.Any(group => group.Name == item.Group.Name))
                return Fail(new(WorkerFailureCategories.TargetNotFound, $"The server '{alias}' lists no group '{item.Group.Name}'."));
        }

        var projects = await ReadAsync<MultiuserServerProjectsInfo>(Inventory("list_server_projects", alias, item.Group, null), info => info.ServerProjects).ConfigureAwait(false);
        if (projects.Error is not null) return Fail(projects.Error);
        var names = projects.Value!.Projects.Select(project => project.Name).ToArray();
        bool SameProject(ProjectServerOperationRequest other) => other.ServerAlias == alias && other.Group?.IsRoot == item.Group.IsRoot
            && other.Group?.Name == item.Group.Name;
        switch (item.Operation)
        {
            case ProjectServerMethods.AddProjectToServer:
                consequences.Add($"Uploads {item.ProjectPath} to {GroupText(item)} of '{alias}' (AddProjectToServer); TIA Portal names the server project "
                    + $"(the file is named '{Path.GetFileNameWithoutExtension(item.ProjectPath)}'). The server lists {names.Length} project(s) there now.");
                return new PlannedState(names, null, earlier.Any(other => SameProject(other) && other.Operation == ProjectServerMethods.AddProjectToServer));
            case ProjectServerMethods.CreateLocalSession:
            case ProjectServerMethods.DeleteLocalSessionFromServer:
            {
                if (!names.Contains(item.ServerProjectName, StringComparer.Ordinal))
                {
                    if (earlier.Any(other => SameProject(other) && other.Operation == ProjectServerMethods.AddProjectToServer)) return new PlannedState(names, null, true);
                    return Fail(new(WorkerFailureCategories.TargetNotFound, $"The server '{alias}' lists no project '{item.ServerProjectName}' in {GroupText(item)}."));
                }

                var sessions = await ReadAsync<MultiuserLocalSessionsInfo>(Inventory("list_local_sessions", alias, item.Group, item.ServerProjectName), info => info.LocalSessions).ConfigureAwait(false);
                if (sessions.Error is not null) return Fail(sessions.Error);
                var ids = sessions.Value!.Sessions.Select(session => session.SessionId).ToArray();
                if (item.Operation == ProjectServerMethods.CreateLocalSession)
                {
                    consequences.Add($"Creates the local session '{item.SessionName}' of '{item.ServerProjectName}' in {item.SessionDirectory} "
                        + $"({item.SessionMode ?? ProjectServerMethods.SessionCreationModes[0]}); {ids.Length} local session(s) are listed now (scope {sessions.Value.Scope}). Open it afterwards with open_project and the reported .als21 path.");
                    return new PlannedState(names, ids, false);
                }

                var session = sessions.Value.Sessions.FirstOrDefault(row => row.SessionId == item.SessionId);
                if (session is null)
                {
                    if (earlier.Any(other => SameProject(other) && other.ServerProjectName == item.ServerProjectName && other.Operation == ProjectServerMethods.CreateLocalSession))
                        return new PlannedState(names, ids, true);
                    return Fail(new(WorkerFailureCategories.TargetNotFound, $"No local session {item.SessionId} of '{item.ServerProjectName}' is listed (scope {sessions.Value.Scope})."));
                }

                consequences.Add($"Deletes the local session {session.SessionId} ({session.ProjectPath}) of '{item.ServerProjectName}' from the server (DeleteLocalSessionFromServer).");
                if (boundPath is not null && string.Equals(ProjectPathNormalization.Canonicalize(session.ProjectPath), boundPath, StringComparison.OrdinalIgnoreCase))
                    fire(ProjectServerGuardDefinitions.BoundSession, $"The local session {session.SessionId} ({session.ProjectPath}) is the project this MCP session is bound to; TIA Portal decides what happens to it.");
                return new PlannedState(names, ids, false);
            }
        }

        return new PlannedState(names, null, false);

        PlannedState? Fail(WriteToolError error)
        {
            _lastFailure = error;
            return null;
        }
    }

    private static void FireServerItemGuards(ProjectServerOperationRequest item, Action<string, string> fire)
    {
        switch (item.Operation)
        {
            case ProjectServerMethods.AddProjectToServer:
                fire(ProjectServerGuardDefinitions.UploadsProject, $"Uploads {item.ProjectPath} to {GroupText(item)} of the Project Server '{item.ServerAlias}'; other users of that server can see and open it.");
                break;
            case ProjectServerMethods.CreateLocalSession:
                fire(ProjectServerGuardDefinitions.CreatesLocalSession, $"Creates the local session '{item.SessionName}' of '{item.ServerProjectName}' on '{item.ServerAlias}' in {item.SessionDirectory}.");
                break;
            case ProjectServerMethods.DeleteLocalSessionFromServer:
                fire(ProjectServerGuardDefinitions.DeletesLocalSession, $"Deletes the local session {item.SessionId} of '{item.ServerProjectName}' from the Project Server '{item.ServerAlias}'; changes not checked in may be lost.");
                break;
        }
    }

    internal static string GroupText(ProjectServerOperationRequest item)
        => item.Group?.IsRoot == true ? "the server root" : $"group '{item.Group?.Name}'";

    public IReadOnlyList<FiredGuard> EvaluateGuards(IReadOnlyList<ProjectServerOperationRequest> items, IReadOnlyList<ItemPlan<ProjectServerWriteEffect>> plans)
        => items.SelectMany(item => _guards.GetValueOrDefault(item.OperationId) ?? []).ToArray();

    /// <summary>Never called: planning simulates the call's own connection changes, so no item depends on another's plan.</summary>
    public Task<ItemReplan<ProjectServerWriteEffect>> ReplanAsync(ProjectServerOperationRequest item)
        => Task.FromResult(ItemReplan<ProjectServerWriteEffect>.Fail(WorkerFailureCategories.WorkerOperationFailed, "project_server_write items are not re-planned."));

    // ============================================================== mutation and projection

    public Task<WorkerCallResult> MutateAsync(ProjectServerOperationRequest item)
        => _write(ProjectServerMethods.ForOperation(item.Operation), ToRequest(item));

    internal static ProjectServerRequestInfo ToRequest(ProjectServerOperationRequest item) => new()
    {
        Operation = item.Operation, ServerAlias = item.ServerAlias, Host = item.Host, Port = item.Port, Protocol = item.Protocol,
        GroupIsRoot = item.Group?.IsRoot, GroupName = item.Group?.Name, ServerProjectName = item.ServerProjectName, ProjectPath = item.ProjectPath,
        SessionName = item.SessionName, SessionDirectory = item.SessionDirectory,
        SessionMode = item.Operation == ProjectServerMethods.CreateLocalSession ? item.SessionMode ?? ProjectServerMethods.SessionCreationModes[0] : null,
        SessionId = item.SessionId, PortalConfirmations = Policy(item),
    };

    internal static string Policy(ProjectServerOperationRequest item) => item.PortalConfirmations ?? PlcDownloadPolicy.PortalConfirmationPolicies.Decline;

    public StructuredOperationItem Project(ProjectServerOperationRequest item, WorkerCallResult result)
    {
        var warnings = result.Warnings ?? Array.Empty<string>();
        if (!result.Success)
        {
            var category = result.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed;
            var failed = StructuredOperationItem.Failed(item, category, result.Error ?? "The Project Server write failed.", warnings);
            if (result.PlcDownloadOutcome is null) return failed;
            try
            {
                var evidence = Decode(item, result.PlcDownloadOutcome);
                _results[item.OperationId] = evidence;
                return failed with { Result = CanonicalJson.ToElement(evidence) };
            }
            catch (JsonException)
            {
                return StructuredOperationItem.Failed(item, WorkerFailureCategories.ProtocolError,
                    $"The worker reported '{category}' for '{item.Operation}' with evidence that did not match its declared result contract; the evidence was rejected.", warnings);
            }
        }

        ProjectServerWriteResultInfo outcome;
        try
        {
            outcome = Decode(item, result.Payload);
        }
        catch (JsonException)
        {
            return StructuredOperationItem.Failed(item, WorkerFailureCategories.ProtocolError,
                $"The worker outcome for '{item.Operation}' did not match its declared result contract and was rejected.", warnings);
        }

        _results[item.OperationId] = outcome;
        var element = CanonicalJson.ToElement(outcome);
        if (outcome.PortalConfirmationsObserved > 0 && outcome.PortalConfirmationPolicy != PlcDownloadPolicy.PortalConfirmationPolicies.Accept)
            return new(item.OperationId, item.Operation, OperationBatchStatus.Failed, element, new StructuredOperationFailure(WorkerFailureCategories.WorkerOperationFailed,
                $"TIA Portal raised {outcome.PortalConfirmationsObserved} confirmation dialog(s) during {item.Operation} and this tool answered each one No "
                + "(see this item's result.portalConfirmations). The Siemens call returned, but declining may have changed what it did, so the item is not "
                + "reported as successful. Inspect with bind_project before retrying; to answer such dialogs Yes, retry with portalConfirmations: \"accept\"."),
                null, null, warnings);
        return new(item.OperationId, item.Operation, OperationBatchStatus.Succeeded, element, null, null, null,
            outcome.PortalConfirmationsObserved > 0
                ? warnings.Append($"TIA Portal raised {outcome.PortalConfirmationsObserved} confirmation dialog(s) during {item.Operation} and each was answered Yes (portalConfirmations: accept); see this item's result.portalConfirmations.").ToArray()
                : warnings);
    }

    private static ProjectServerWriteResultInfo Decode(ProjectServerOperationRequest item, string payload)
    {
        var outcome = CanonicalJson.DeserializeWorkerPayload<ProjectServerWriteResultInfo>(payload);
        if (outcome is null || outcome.Operation != item.Operation || outcome.Applied is null || outcome.Applied.Any(call => call is null)
            || outcome.ServerAlias != item.ServerAlias)
            throw new JsonException();
        PlcOnlinePayloadContract.ValidatePortalConfirmations(Policy(item), outcome.PortalConfirmationPolicy, outcome.PortalConfirmations,
            outcome.PortalConfirmationsObserved, outcome.PortalConfirmationsComplete);
        return outcome;
    }

    // ============================================================== verification and response

    public async Task<WriteVerification?> VerifyAsync(StructuredOperationBatch batch)
        => await new TrustedWriteVerifier<ProjectServerOperationRequest>(_items, new ProjectServerVerificationPolicy(this))
            .VerifyAsync(batch).ConfigureAwait(false);

    public bool VerificationSucceeded(WriteVerification? verification) => verification?.Success == true;

    public ProjectServerGuardedWriteResponse Compose(WriteReport<ProjectServerWriteEffect, WriteVerification> report)
        => ProjectServerWritePayloadBudget.Apply(new(ToolName, ContractVersion, report.Phase, report.Success, report.Error,
            report.Warnings.Concat(_planWarnings).Distinct(StringComparer.Ordinal).ToArray(), report.Guards,
            report.Effects.Select(e => new ProjectServerWriteEffectPresentation(e.OperationId, e.Effect, null)).ToArray(),
            report.Batch, report.Verification, null));

    // ============================================================== inventory reads

    internal WorkerRequest Inventory(string action, string? alias, Tools.MultiuserGroupSelector? group, string? project)
        => new()
        {
            Method = action, PortalProcessId = _portalProcessId, MultiuserServerAlias = alias, MultiuserGroupIsRoot = group?.IsRoot,
            MultiuserGroupName = group?.Name, MultiuserServerProjectName = project,
        };

    internal async Task<(T? Value, WriteToolError? Error)> ReadAsync<T>(WorkerRequest request, Func<ProjectBindingInspectionInfo, T?> select)
        where T : class
    {
        var result = await _inspect(request).ConfigureAwait(false);
        foreach (var warning in result.Warnings ?? Array.Empty<string>())
            if (!_planWarnings.Contains(warning, StringComparer.Ordinal)) _planWarnings.Add(warning);
        var decoded = ProjectBindingInspectionPayloadContract.Decode(request, result);
        if (decoded.Failure is { } failure) return (null, new(failure.Category, $"bind_project {request.Method}: {failure.Message}"));
        return decoded.Value is { } info && select(info) is { } value
            ? (value, null)
            : (null, new(WorkerFailureCategories.ProtocolError, $"bind_project {request.Method} returned no typed inventory."));
    }

    private static WriteValidation Invalid(string message) => WriteValidation.Invalid(WorkerFailureCategories.ValidationError, message);
}
