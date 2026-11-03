using Siemens.Engineering;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Project;

using Project = Siemens.Engineering.Project;

public class TiaPortalSession : IDisposable
{
    private readonly bool _allowTiaConfirmations;
    private readonly string _workerSessionId = Guid.NewGuid().ToString("N");
    private readonly object _portalHandleSync = new();
    private readonly object _confirmationSync = new();
    private ConfirmationScope? _confirmationScope;
    private readonly List<string> _endedLateConfirmations = new();
    private readonly List<string> _unscopedConfirmationTexts = new();
    private int _unscopedConfirmations;
    private readonly object _notificationSync = new();
    private bool _requestRunning;
    private readonly List<PortalNotificationInfo> _requestNotifications = new();
    private readonly List<string> _lateNotificationTexts = new();
    private int _lateNotifications;
    // Not a ConfirmationResult: a Siemens value-type field makes loading this type load Siemens.Engineering, and the
    // worker's static session is created before AssemblyResolver.Register runs (the worker then crashed at startup).
    private bool _unscopedAnswerYes;
    private TiaPortal? _tiaPortal;
    private EventHandler<EventArgs>? _portalDisposedHandler;
    private TiaPortal? _unreleasedTemporaryTarget;
    private ActiveProjectContext? _activeContext;
    private bool _disposed;
    private int? _attachedProcessId;
    private string? _selectedProjectPath;
    private long _sessionGeneration;
    private long _portalAttachmentRevision;
    private long _portalDisposalRevision;

    public TiaPortalSession(bool allowTiaConfirmations = false)
    {
        // Even when a caller requests automatic confirmations, an explicitly configured
        // read-only worker must reject every TIA confirmation dialog. This keeps the final
        // Siemens-facing layer fail-closed if a nominally read operation unexpectedly asks
        // TIA Portal to confirm a state-changing action.
        AccessMode = WorkerOperationAuthorization.ParseAccessMode(Environment.GetCommandLineArgs());
        _allowTiaConfirmations = allowTiaConfirmations &&
            WorkerOperationAuthorization.AllowsTiaConfirmations(AccessMode);
    }

    internal ActiveProjectContext? ActiveContext => _activeContext;
    internal McpAccessMode AccessMode { get; }
    internal ProjectBase? EngineeringRoot => _activeContext?.EngineeringRoot;

    /// <summary>Standalone-only compatibility bridge for existing content services.</summary>
    public Project? Project => (_activeContext?.Owner as StandaloneProjectOwner)?.Project;

    internal StandaloneProjectOwner RequireStandaloneOwner()
    {
        if (_activeContext is null)
            throw new InvalidOperationException(ProjectOpenPolicy.NoProjectOpenMessage(AccessMode));
        if (_activeContext.Owner is StandaloneProjectOwner owner)
            return owner;
        throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
            "This operation requires a standalone project. The active local session must use its explicit lifecycle operations.");
    }

    public TiaPortal? TiaPortal => _tiaPortal;

    public bool IsConnected => _tiaPortal != null;

    public int? CurrentProcessId => _attachedProcessId;
    internal long PortalAttachmentRevision => _portalAttachmentRevision;
    internal long PortalDisposalRevision
    {
        get
        {
            // Publish disposal evidence only after the callback finishes clearing the attachment.
            lock (_portalHandleSync)
                return _portalDisposalRevision;
        }
    }

    public WorkerSessionIdentity GetSessionIdentity()
    {
        // Identity describes the live Siemens handle, not the sticky path retained for a later
        // explicit re-scan. If the handle was closed in the UI, TryReadCurrentProjectPath clears
        // it and advances the generation; returning the retained path here would falsely certify
        // a project that is no longer open.
        var liveProjectPath = ProjectPathNormalization.Canonicalize(TryReadCurrentProjectPath());
        return new WorkerSessionIdentity
        {
            WorkerSessionId = _workerSessionId,
            SessionGeneration = Interlocked.Read(ref _sessionGeneration),
            PortalProcessId = _attachedProcessId,
            ProjectPath = liveProjectPath,
            Context = _activeContext?.Owner is LocalSessionOwner ? _activeContext.ToInfo() : null
        };
    }

    public void ValidateExpectedSessionIdentity(
        WorkerSessionIdentity? expected,
        bool allowMissingExpectedIdentity,
        bool useCachedIdentity = false)
    {
        if (expected is null)
        {
            if (allowMissingExpectedIdentity)
            {
                return;
            }

            throw new WorkerOperationException(
                WorkerFailureCategories.BindingConflict,
                "This operation requires a verified project binding. Use bind_project first, or "
                + "configure --project and let the host verify it before this operation. In read-write/full mode, "
                + "you can also use open_project or create_project.");
        }

        var current = useCachedIdentity ? GetCachedSessionIdentity() : GetSessionIdentity();
        if (string.Equals(expected.WorkerSessionId, current.WorkerSessionId, StringComparison.Ordinal)
            && expected.SessionGeneration == current.SessionGeneration
            && expected.PortalProcessId == current.PortalProcessId
            && PathsEqual(expected.ProjectPath, current.ProjectPath))
        {
            return;
        }

        throw new WorkerOperationException(
            WorkerFailureCategories.BindingConflict,
            "The expected Worker/TIA/project session identity no longer matches the live worker session. "
            + $"Expected worker='{expected.WorkerSessionId}', generation={expected.SessionGeneration}, "
            + $"PID={FormatProcessId(expected.PortalProcessId)}, project='{expected.ProjectPath ?? "(none)"}'; "
            + $"current worker='{current.WorkerSessionId}', generation={current.SessionGeneration}, "
            + $"PID={FormatProcessId(current.PortalProcessId)}, project='{current.ProjectPath ?? "(none)"}'. "
            + "Refresh project status and obtain a new explicitly verified binding before retrying.");
    }

    internal void ValidateEmptyPortal(WorkerSessionIdentity expected)
    {
        ThrowIfDisposed();
        if (!IsConnected || expected.ProjectPath is not null || expected.Context is not null
            || string.IsNullOrWhiteSpace(expected.WorkerSessionId) || expected.PortalProcessId is null or <= 0
            || expected.SessionGeneration < 0)
            throw new WorkerOperationException(WorkerFailureCategories.BindingConflict,
                "Empty-Portal verification requires the complete already-attached Portal identity.");
        ValidateExpectedSessionIdentity(expected, false);
        ValidatePortalProcess();
        if (ReadOpenContexts().Count != 0)
            throw new WorkerOperationException(WorkerFailureCategories.BindingConflict,
                "The attached Portal contains an open owner. No project was selected or opened.");
        ValidatePortalProcess();
        ValidateExpectedSessionIdentity(expected, false);
    }

    public void Connect(string? requestedProjectPath, int? requestedProcessId = null)
    {
        ThrowIfDisposed();
        if (requestedProcessId.HasValue && requestedProcessId <= 0)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                "PortalProcessId must be positive.");

        if (IsConnected)
        {
            if (requestedProcessId.HasValue && requestedProcessId != _attachedProcessId)
                throw new WorkerOperationException(WorkerFailureCategories.BindingConflict,
                    "The requested Portal PID differs from the attached Portal. Use explicit selection to switch.");
            return;
        }

        if (requestedProcessId.HasValue)
        {
            EnsurePortalConnected(requestedProcessId);
            SelectOpenProject(ProjectPathNormalization.Canonicalize(requestedProjectPath));
            return;
        }

        var inventory = TiaPortalProcessInventory.Read();
        var candidates = inventory.Select(entry => entry.Candidate).ToList();
        var selectedProcessId = TiaPortalTargetSelector.SelectProcessId(candidates, requestedProjectPath);
        var entry = inventory.FirstOrDefault(item => item.Candidate.Id == selectedProcessId);
        var selectedProcess = entry?.Process;

        if (selectedProcess is null)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.PostconditionFailed,
                $"TIA Portal selector chose PID {selectedProcessId}, but that process was no longer available for Attach().");
        }

        AttachPortal(selectedProcess, selectedProcessId);
        // Projects present when we attach belong to the TIA Portal UI, never this worker.
        SelectOpenProject(ProjectPathNormalization.Canonicalize(requestedProjectPath));

        Console.Error.WriteLine(
            $"Connected to TIA Portal PID {_attachedProcessId}"
            + $" with project '{CurrentProjectPath ?? "(none)"}'.");
    }

    private void AttachPortal(TiaPortalProcess selectedProcess, int selectedProcessId)
    {
        ReleaseTemporaryTargetIfSafe();
        var attachedPortal = selectedProcess.Attach();
        int attachedProcessId;
        try { attachedProcessId = attachedPortal.GetCurrentProcess().Id; }
        catch (Exception)
        {
            _unreleasedTemporaryTarget = attachedPortal;
            ReleaseTemporaryTargetIfSafe();
            throw new WorkerOperationException(WorkerFailureCategories.BindingConflict,
                "TIA Portal Attach() returned a handle whose PID could not be verified. No project was selected.");
        }
        if (attachedProcessId != selectedProcessId)
        {
            _unreleasedTemporaryTarget = attachedPortal;
            ReleaseTemporaryTargetIfSafe();
            throw new WorkerOperationException(
                WorkerFailureCategories.BindingConflict,
                $"TIA Portal Attach() targeted PID {selectedProcessId} but returned PID {attachedProcessId}. No operation was performed.");
        }

        SetPortalHandle(attachedPortal, attachedProcessId);
        SubscribeToPortal(attachedPortal);
    }

    /// <summary>Validates or establishes only the persistent Portal attachment; never selects a project.</summary>
    public void EnsurePortalConnected(int? requestedProcessId = null)
    {
        ThrowIfDisposed();
        if (requestedProcessId.HasValue && requestedProcessId.Value <= 0)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "PortalProcessId must be positive.");

        if (IsConnected)
        {
            // A deliberate foreign-PID refusal precedes even the live handle check.
            if (requestedProcessId.HasValue && requestedProcessId != _attachedProcessId)
                throw new WorkerOperationException(WorkerFailureCategories.BindingConflict,
                    "Inventory is limited to the currently attached Portal. Use explicit binding to select another instance.");
            ValidatePortalProcess();
            return;
        }

        var inventory = TiaPortalProcessInventory.Read();
        var matches = inventory.Where(entry => !requestedProcessId.HasValue || entry.Candidate.Id == requestedProcessId).ToList();
        if (matches.Count == 0)
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, "No matching running TIA Portal instance was found.");
        if (matches.Count != 1)
            throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous, "Multiple running TIA Portal instances require an exact PortalProcessId.");
        AttachPortal(matches[0].Process, matches[0].Candidate.Id);
    }

    private void ValidatePortalProcess()
    {
        int actualProcessId;
        try { actualProcessId = _tiaPortal!.GetCurrentProcess().Id; }
        catch (Exception)
        {
            // Forget dead evidence without attaching, detaching, or disposing any project/session.
            ClearPortal(_tiaPortal, unexpectedDisposal: false);
            throw new WorkerOperationException(WorkerFailureCategories.BindingConflict, "The attached TIA Portal instance is no longer verifiable.");
        }
        if (_attachedProcessId != actualProcessId)
        {
            var expectedProcessId = _attachedProcessId;
            SetPortalHandle(_tiaPortal, actualProcessId);
            throw new WorkerOperationException(WorkerFailureCategories.BindingConflict,
                $"The attached TIA Portal PID changed from {FormatProcessId(expectedProcessId)} to {actualProcessId}. No operation was performed.");
        }
    }

    private WorkerSessionIdentity GetCachedSessionIdentity()
        => new()
        {
            WorkerSessionId = _workerSessionId,
            SessionGeneration = Interlocked.Read(ref _sessionGeneration),
            PortalProcessId = _attachedProcessId,
            ProjectPath = _selectedProjectPath,
            Context = _activeContext?.Owner is LocalSessionOwner ? _activeContext.ToInfo() : null
        };

    public PortalProjectSelectionInfo SelectPortalProject(string projectPath)
        => SelectPortalProject(projectPath, null);

    public PortalProjectSelectionInfo SelectPortalProject(string? projectPath, int? requestedProcessId)
    {
        ThrowIfDisposed();
        ReleaseTemporaryTargetIfSafe();

        // Lookup precedes every session transition, including the first attach and same-PID selection.
        var inventory = TiaPortalProcessInventory.Read();
        if (requestedProcessId.HasValue && requestedProcessId <= 0)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                "PortalProcessId must be positive.");
        var selectedProcessId = requestedProcessId
            ?? (projectPath is null
                ? throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous,
                    "An exact PortalProcessId is required when no projectPath identifies a Portal.")
                : TiaPortalTargetSelector.SelectExactProcessId(
                    inventory.Select(entry => entry.Candidate).ToList(), projectPath));
        if (!inventory.Any(entry => entry.Candidate.Id == selectedProcessId))
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                "No running TIA Portal instance has the requested PortalProcessId.");
        var selectedProcess = inventory.First(entry => entry.Candidate.Id == selectedProcessId).Process;
        var previous = new PortalProjectSelectionInfo
        {
            PreviousProcessId = _attachedProcessId,
            PreviousProjectPath = _selectedProjectPath,
            PreviousProjectWasWorkerOpened = _activeContext?.Owner.OpenedByWorker == true,
            PreviousProjectIsModified = TryReadProjectIsModified()
        };

        if (IsConnected && _attachedProcessId == selectedProcessId)
        {
            var projects = ReadOpenContexts();
            var selectedIndex = TiaPortalTargetSelector.SelectProjectIndex(
                projects.Select(candidate => (string?)candidate.BindingPath).ToList(), projectPath);
            if (selectedIndex is null)
            {
                throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                    $"Requested project '{projectPath}' is no longer open in TIA Portal PID {selectedProcessId}. No project was selected.");
            }

            AdoptContext(projects[selectedIndex.Value].Context, projectPath);
            return previous;
        }

        // A temporary target attachment must itself have a known retaining client. Otherwise a
        // missing/ambiguous owner could leave us unable to release the last headless local client.
        if (!HasRetainingClient(selectedProcess))
            throw new WorkerOperationException(WorkerFailureCategories.GuardBlocked,
                "The target Portal has no verified retaining client for a temporary attachment.");

        TiaPortal? target = null;
        var promoted = false;
        try
        {
            target = selectedProcess.Attach();
            var actualProcessId = target.GetCurrentProcess().Id;
            if (actualProcessId != selectedProcessId)
                throw new InvalidOperationException($"TIA Portal Attach() targeted PID {selectedProcessId} but returned PID {actualProcessId}.");

            var projects = ReadOpenContexts(target);
            var selectedIndex = TiaPortalTargetSelector.SelectProjectIndex(
                projects.Select(candidate => (string?)candidate.BindingPath).ToList(), projectPath);
            if (selectedIndex is null)
                throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                    $"Requested project '{projectPath}' is no longer open in TIA Portal PID {selectedProcessId}.");

            var selectedOwner = projects[selectedIndex.Value].Context;
            var verified = ReadOpenContexts(target);
            var verifiedIndex = TiaPortalTargetSelector.SelectProjectIndex(
                verified.Select(candidate => (string?)candidate.BindingPath).ToList(), projectPath);
            if (verifiedIndex is null)
                throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                    "The target owner disappeared before source release.");
            var verifiedOwner = verified[verifiedIndex.Value].Context;
            if (selectedOwner.Owner.GetType() != verifiedOwner.Owner.GetType()
                || !object.Equals(selectedOwner.EngineeringRoot, verifiedOwner.EngineeringRoot)
                || selectedOwner.Owner is LocalSessionOwner firstLocal
                && verifiedOwner.Owner is LocalSessionOwner secondLocal
                && !object.Equals(firstLocal.LocalSession, secondLocal.LocalSession))
                throw new WorkerOperationException(WorkerFailureCategories.BindingConflict,
                    "The target owner changed before source release.");

            if (IsConnected)
                Disconnect();

            SetPortalHandle(target, actualProcessId);
            SubscribeToPortal(target);
            promoted = true;

            AdoptContext(verifiedOwner, projectPath);
            previous.Reattached = true;
            return previous;
        }
        catch (Exception ex)
        {
            if (!promoted && target is not null)
            {
                var refusal = EvaluatePortalDetach(target);
                if (refusal is not null)
                {
                    _unreleasedTemporaryTarget = target;
                    throw new WorkerOperationException(WorkerFailureCategories.GuardBlocked,
                        "The temporary target Portal cannot be safely released: " + refusal);
                }
                target.Dispose();
            }
            if (ex is WorkerOperationException operation)
                throw operation;
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                $"Could not select the requested Portal project: {ex.Message}");
        }
    }

    private bool? TryReadProjectIsModified()
        => TryReadProjectIsModified(EngineeringRoot);

    private static bool? TryReadProjectIsModified(ProjectBase? project)
    {
        try { return project?.IsModified; }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not read project modified state: {ex.Message}");
            return null;
        }
    }

    private static IReadOnlyList<bool?>? ReadAttachedProjectModifiedStates(TiaPortal portal)
    {
        try { return portal.Projects.Select(TryReadProjectIsModified).ToList(); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not read attached Portal projects: {ex.Message}");
            return null;
        }
    }

    public void OpenProject(string projectPath)
    {
        ThrowIfDisposed();
        if (_activeContext?.ContainerKind == ProjectContainerKinds.ServerProject)
            throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
                "A server-project owner cannot be replaced through open_project.");
        if (string.IsNullOrWhiteSpace(projectPath) || !Path.IsPathRooted(projectPath))
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                "ProjectPath must be an absolute .ap21 or .als21 file path.");
        var extension = Path.GetExtension(projectPath);
        var isLocal = string.Equals(extension, ".als21", StringComparison.OrdinalIgnoreCase);
        if (!isLocal && !string.Equals(extension, ".ap21", StringComparison.OrdinalIgnoreCase))
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                "Only .ap21 and .als21 files can be opened.");
        if (!File.Exists(projectPath))
            throw new FileNotFoundException("TIA Portal project file was not found.", projectPath);
        var requestedPath = Path.GetFullPath(projectPath);

        if (!IsConnected)
            Connect(requestedPath);

        var sourceContext = _activeContext;
        var currentPath = TryReadCurrentProjectPath();
        if (sourceContext is not null && _activeContext is null)
            throw new WorkerOperationException(WorkerFailureCategories.BindingConflict,
                "The selected source owner changed before the open. No opener was called.");

        var openContexts = ReadOpenContexts();
        if (isLocal)
        {
            if (_activeContext?.Owner is LocalSessionOwner existingLocal
                && PathsEqual(_activeContext.SessionContainerPath, requestedPath))
            {
                // The exact input belongs to the continuously verified selected owner.
                if (openContexts.Count(candidate => candidate.Context.Owner is LocalSessionOwner owner
                    && object.Equals(owner.LocalSession, existingLocal.LocalSession)
                    && object.Equals(owner.Project, existingLocal.Project)) != 1)
                    throw new WorkerOperationException(WorkerFailureCategories.BindingConflict,
                        "The previously opened local-session owner is no longer unique.");
                return;
            }

            if (_activeContext?.Owner is LocalSessionOwner)
                throw new WorkerOperationException(WorkerFailureCategories.GuardBlocked,
                    "local_session_requires_terminal_operation: another session cannot be opened while a local owner is selected.");

            // An unrelated owner could be this ALS destination; the installed API exposes no
            // reverse owner-to-ALS join. Empty Portal is the only qualified opening case.
            var currentStandalone = _activeContext?.Owner as StandaloneProjectOwner;
            if (openContexts.Any(candidate => currentStandalone is null
                || !object.Equals(candidate.Context.EngineeringRoot, currentStandalone.Project)))
                throw new WorkerOperationException(WorkerFailureCategories.GuardBlocked,
                    "An already-open owner prevents proving that this ALS destination is unopened.");
            if (currentStandalone is not null && !currentStandalone.OpenedByWorker)
                throw new WorkerOperationException(WorkerFailureCategories.GuardBlocked,
                    "Preservation of the user-opened source during LocalSessions.Open is unproved.");

            if (currentStandalone is not null)
            {
                if (openContexts.Count != 1
                    || !object.Equals(openContexts[0].Context.EngineeringRoot, currentStandalone.Project))
                    throw new WorkerOperationException(WorkerFailureCategories.BindingConflict,
                        "The selected standalone source is no longer the sole open owner.");
                var sourceIdentity = GetSessionIdentity();
                ValidateExpectedSessionIdentity(sourceIdentity, false);
                var beforeClose = ReadOpenContexts();
                if (beforeClose.Count != 1
                    || !object.Equals(beforeClose[0].Context.EngineeringRoot, currentStandalone.Project))
                    throw new WorkerOperationException(WorkerFailureCategories.BindingConflict,
                        "The selected standalone source changed before close.");
                ProjectRebindCloseGuard.CloseBeforeRebind(
                    () => currentStandalone.Project.IsModified,
                    () => currentStandalone.Project.Close(),
                    () => SetActiveContext(null));
            }

            if (ReadOpenContexts().Count != 0)
                throw new WorkerOperationException(currentStandalone is null
                    ? WorkerFailureCategories.GuardBlocked : WorkerFailureCategories.PostconditionFailed,
                    currentStandalone is null
                        ? "The Portal gained an owner before LocalSessions.Open; no opener was called."
                        : "The Portal is not empty after source close. Inspect the possible mutation before retrying.");

            var openedOwner = _tiaPortal!.LocalSessions.Open(new FileInfo(requestedPath));
            var openedContext = LocalSessionContextResolver.Resolve(openedOwner, true);
            var afterOpen = ReadOpenContexts();
            if (afterOpen.Count != 1 || afterOpen[0].Context.Owner is not LocalSessionOwner liveOwner
                || !object.Equals(liveOwner.LocalSession, openedOwner)
                || !object.Equals(liveOwner.Project, openedContext.EngineeringRoot)
                || !PathsEqual(afterOpen[0].BindingPath, openedContext.BindingPath))
                throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                    "LocalSessions.Open returned an owner that could not be uniquely verified. The open may have succeeded; inspect before retrying.");
            openedContext.RecordSuccessfulOpen(requestedPath);
            AdoptContext(openedContext, openedContext.BindingPath);
            return;
        }

        if (_activeContext?.Owner is LocalSessionOwner)
            throw new WorkerOperationException(WorkerFailureCategories.GuardBlocked,
                "local_session_requires_terminal_operation: another project cannot be opened while a local owner is selected.");

        if (currentPath is not null && PathsEqual(currentPath, requestedPath))
            return;

        var alreadyOpen = openContexts.Where(candidate => PathsEqual(candidate.BindingPath, requestedPath)).ToList();
        if (alreadyOpen.Count > 1)
            throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous,
                "Multiple already-open projects have the requested path.");
        if (alreadyOpen.Count == 1)
        {
            if (_activeContext?.Owner.OpenedByWorker == true)
                throw new WorkerOperationException(WorkerFailureCategories.GuardBlocked,
                    "Cannot abandon worker ownership of another open project during destination reuse.");
            AdoptContext(alreadyOpen[0].Context, requestedPath);
            return;
        }

        if (openContexts.Any(candidate => candidate.Context.Owner is LocalSessionOwner))
            throw new WorkerOperationException(WorkerFailureCategories.GuardBlocked,
                "Preservation of an already-open local session during Projects.Open is unproved.");

        if (Project is not null)
        {
            if (_activeContext?.Owner.OpenedByWorker == true)
            {
                var currentProject = RequireStandaloneOwner().Project;
                try
                {
                    ProjectRebindCloseGuard.CloseBeforeRebind(
                        () => currentProject.IsModified,
                        () =>
                        {
                            Console.Error.WriteLine($"Closing project '{currentPath ?? "(unknown)"}' before opening '{requestedPath}'.");
                            currentProject.Close();
                        },
                        () =>
                        {
                            SetActiveContext(null);
                            var openedProject = _tiaPortal!.Projects.Open(new FileInfo(requestedPath));
                            AdoptProject(openedProject, openedByWorker: true, requestedPath);
                        });
                    return;
                }
                catch (InvalidOperationException ex)
                {
                    Console.Error.WriteLine($"Could not close the previous project: {ex.Message}");
                    throw;
                }
            }
            else
            {
                // The user opened this project in the TIA Portal UI; it is not ours to close.
                Console.Error.WriteLine($"Leaving user-opened project '{currentPath ?? "(unknown)"}' open; opening '{requestedPath}' alongside it.");
            }

        }

        var project = _tiaPortal!.Projects.Open(new FileInfo(requestedPath));
        AdoptProject(project, openedByWorker: true, requestedPath);
    }

    /// <summary>Absolute path of the attached project, or null when nothing is attached.</summary>
    public string? CurrentProjectPath => TryReadCurrentProjectPath();

    /// <summary>Reads the selected project's rebind state without opening or closing a project.</summary>
    internal ProjectRebindStateInfo ReadProjectRebindState(string destinationProjectPath)
    {
        ThrowIfDisposed();
        if (_activeContext?.ContainerKind == ProjectContainerKinds.ServerProject)
            throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
                "A server-project owner cannot be used for this rebind probe.");

        var destination = ProjectPathNormalization.Canonicalize(destinationProjectPath)
            ?? throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "A destination project path is required for the rebind-state probe.");
        var source = ProjectPathNormalization.Canonicalize(TryReadCurrentProjectPath());
        if (source is null)
        {
            return ProjectRebindStateInfo.Create(null, destination, null, sourceOpenedByWorker: false);
        }

        var currentProject = EngineeringRoot;
        if (currentProject is null)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.WorkerOperationFailed,
                "The selected source project became unavailable during the rebind-state probe.");
        }

        bool isModified;
        try
        {
            isModified = currentProject.IsModified;
        }
        catch (Exception)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.WorkerOperationFailed,
                "Could not verify whether the selected source project has unsaved changes. "
                + "No project was opened or closed.");
        }

        return ProjectRebindStateInfo.Create(source, destination, isModified,
            _activeContext?.Owner.OpenedByWorker == true,
            _activeContext?.Owner is LocalSessionOwner ? _activeContext.ToInfo() : null);
    }

    internal void TrackWorkerOpenedProject(Project project)
        => AdoptProject(project, openedByWorker: true, expectedProjectPath: null);

    /// <summary>
    /// Accepts an authorized in-place path transition such as Siemens SaveAs. The project handle
    /// stays the same, so the generation must be advanced explicitly when its identity changes.
    /// </summary>
    internal void AcceptCurrentProjectIdentity()
    {
        var currentPath = ProjectPathNormalization.Canonicalize(TryReadCurrentProjectPath());
        if (currentPath is null)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.PostconditionFailed,
                "TIA Portal did not expose a project path after the authorized project transition.");
        }

        if (!PathsEqual(_selectedProjectPath, currentPath))
        {
            _selectedProjectPath = currentPath;
            IncrementGeneration();
        }
    }

    private string? TryReadCurrentProjectPath()
    {
        if (EngineeringRoot is null)
        {
            return null;
        }

        if (_activeContext?.Owner is LocalSessionOwner localOwner)
        {
            try
            {
                var matching = ReadOpenContexts().Where(candidate =>
                    candidate.Context.Owner is LocalSessionOwner
                    && PathsEqual(candidate.BindingPath, _selectedProjectPath)).ToList();
                if (matching.Count != 1
                    || matching[0].Context.Owner is not LocalSessionOwner liveOwner
                    || !object.Equals(localOwner.LocalSession, liveOwner.LocalSession)
                    || !object.Equals(localOwner.Project, liveOwner.Project))
                {
                    SetActiveContext(null);
                    return null;
                }
            }
            catch (WorkerOperationException)
            {
                SetActiveContext(null);
                throw;
            }
        }

        try
        {
            var currentPath = EngineeringRoot.Path?.FullName;
            if (!string.IsNullOrWhiteSpace(currentPath))
            {
                return currentPath;
            }

            // A live project used for a bound engineering operation must always have a readable
            // identity. Treat a null/blank path exactly like a stale handle: clearing the handle
            // advances the generation, so the post-refresh ExpectedSessionIdentity check fails
            // before the operation body can touch an unidentified project.
            SetActiveContext(null);
            return null;
        }
        catch (EngineeringException)
        {
            // Stale handle: the project was closed in the TIA Portal UI since we opened it.
            SetActiveContext(null);
            return null;
        }
    }

    internal void MarkProjectClosed()
    {
        var hadProjectHandle = _activeContext is not null;
        var hadSelectedProject = _selectedProjectPath is not null;
        SetActiveContext(null);
        _selectedProjectPath = null;
        if (hadSelectedProject && !hadProjectHandle)
        {
            // SetActiveContext already advanced the generation when a live handle was cleared.
            // If the handle had already gone stale, clearing the retained identity is itself the
            // observable session transition that invalidates earlier safety evidence.
            IncrementGeneration();
        }
    }

    public void EnsureConnected(string? requestedProjectPath, int? requestedProcessId = null)
    {
        ThrowIfDisposed();
        if (requestedProcessId.HasValue && requestedProcessId <= 0)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                "PortalProcessId must be positive.");

        if (!IsConnected)
        {
            Connect(requestedProjectPath, requestedProcessId);
            return;
        }

        if (requestedProcessId.HasValue && requestedProcessId != _attachedProcessId)
            throw new WorkerOperationException(WorkerFailureCategories.BindingConflict,
                "The requested Portal PID differs from the attached Portal. Use explicit selection to switch.");

        ValidatePortalProcess();

        if (_activeContext is not null)
        {
            // The bound project may have been closed in the TIA Portal UI since we last read
            // it. Detect that now, in the same call, instead of leaving it for whichever call
            // happens to touch Project.Path next (e.g. CurrentProjectPath) — otherwise a caller
            // sees one stale response (nothing bound) before a *second* request finally
            // rescans and picks up whatever project is open now. Openness has no "project
            // closed" event to push this proactively, so detect-then-rescan has to happen
            // within a single EnsureConnected() call.
            var actualPath = ProjectPathNormalization.Canonicalize(TryReadCurrentProjectPath());
            if (actualPath is not null && _selectedProjectPath is not null
                && !PathsEqual(actualPath, _selectedProjectPath))
            {
                var previousPath = _selectedProjectPath;
                _selectedProjectPath = actualPath;
                IncrementGeneration();
                throw new WorkerOperationException(
                    WorkerFailureCategories.BindingConflict,
                    $"The selected TIA project changed outside this worker from '{previousPath}' "
                    + $"to '{actualPath}'. No operation was performed.");
            }
        }

        if (_activeContext is null)
        {
            // Nothing bound — either there never was a project, or the probe above just found
            // the previous handle stale. A project may have been opened in the TIA Portal UI —
            // re-scan instead of requiring a worker restart to pick it up. The exact requested
            // or retained path is authoritative; a different sole project is never adopted as a
            // fallback after a previously selected project disappears.
            SelectOpenProject(
                ProjectPathNormalization.Canonicalize(requestedProjectPath) ?? _selectedProjectPath);
        }
    }

    private void SelectOpenProject(string? expectedProjectPath)
    {
        if (_tiaPortal is null)
        {
            return;
        }

        var candidates = ReadOpenContexts();
        var selectedIndex = TiaPortalTargetSelector.SelectProjectIndex(
            candidates.Select(candidate => (string?)candidate.BindingPath).ToList(), expectedProjectPath);
        if (selectedIndex is null)
        {
            SetActiveContext(null);
            return;
        }

        AdoptContext(candidates[selectedIndex.Value].Context, expectedProjectPath);
    }

    private static bool ReadAttachedLocalSessionPresence(TiaPortal portal)
    {
        try { return portal.LocalSessions.Any(); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not read attached Portal local sessions: {ex.Message}");
            return true;
        }
    }

    private static bool HasRetainingClient(TiaPortalProcess process)
    {
        try
        {
            return TiaPortalProcessInventory.TryReadHasUserInterface(process)
                || process.AttachedSessions.Any(session =>
                    session.ProcessId != System.Diagnostics.Process.GetCurrentProcess().Id);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string? EvaluatePortalDetach(TiaPortal portal)
    {
        var hasUserInterface = false;
        var otherClientCount = 0;
        try
        {
            var process = portal.GetCurrentProcess();
            hasUserInterface = TiaPortalProcessInventory.TryReadHasUserInterface(process);
            var workerProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;
            otherClientCount = process.AttachedSessions.Count(session => session.ProcessId != workerProcessId);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not read attached Portal clients: {ex.Message}");
        }

        return PortalDetachGuard.EvaluateProjects(hasUserInterface, otherClientCount,
            ReadAttachedProjectModifiedStates(portal), ReadAttachedLocalSessionPresence(portal));
    }

    private void ReleaseTemporaryTargetIfSafe()
    {
        if (_unreleasedTemporaryTarget is null)
            return;
        var refusal = EvaluatePortalDetach(_unreleasedTemporaryTarget);
        if (refusal is not null)
            throw new WorkerOperationException(WorkerFailureCategories.GuardBlocked,
                "The retained temporary target Portal cannot be safely released: " + refusal);
        _unreleasedTemporaryTarget.Dispose();
        _unreleasedTemporaryTarget = null;
    }

    internal IReadOnlyList<OpenProjectContextCandidate> ReadOpenContexts()
    {
        if (_tiaPortal is null)
            return Array.Empty<OpenProjectContextCandidate>();

        return ReadOpenContexts(_tiaPortal);
    }

    private static IReadOnlyList<OpenProjectContextCandidate> ReadOpenContexts(TiaPortal portal)
    {
        var contexts = new List<OpenProjectContextCandidate>();
        foreach (var project in portal.Projects)
        {
            // A secondary (read-only reference) project is never a binding candidate (issue #20), and neither is a project whose
            // IsPrimary cannot be read (security review U3, fail safe): only IsPrimary == true is a candidate.
            var primary = ReadIsPrimary(project);
            if (!Contracts.Library.LibraryPolicy.IsBindingCandidate(primary))
            {
                if (primary is null)
                    Console.Error.WriteLine("An open project's ProjectBase.IsPrimary could not be read; it is not offered for binding (it may be a secondary project).");
                continue;
            }

            contexts.Add(new OpenProjectContextCandidate(ActiveProjectContext.ForStandalone(project, false)));
        }
        foreach (var owner in portal.LocalSessions)
            contexts.Add(new OpenProjectContextCandidate(LocalSessionContextResolver.Resolve(owner, false)));
        return contexts;
    }

    /// <summary><c>ProjectBase.IsPrimary</c> (V21; runtime lookup, not on the stubs); null when absent or unreadable.</summary>
    private static bool? ReadIsPrimary(Project project)
    {
        try
        {
            return project.GetType().GetProperty("IsPrimary")?.GetValue(project) as bool?;
        }
        catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException is EngineeringException)
        {
            return null;
        }
    }

    private void AdoptProject(Project project, bool openedByWorker, string? expectedProjectPath)
        => AdoptContext(ActiveProjectContext.ForStandalone(project, openedByWorker), expectedProjectPath);

    internal void AdoptContext(ActiveProjectContext context, string? expectedProjectPath)
    {
        ThrowIfDisposed();
        if (context is null) throw new ArgumentNullException(nameof(context));
        var actualPath = ProjectPathNormalization.Canonicalize(TryReadProjectPathForSelection(context.EngineeringRoot));
        if (actualPath is null)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.PostconditionFailed,
                "TIA Portal returned a project handle without a readable absolute path. No project was selected.");
        }

        var expectedPath = ProjectPathNormalization.Canonicalize(expectedProjectPath);
        if (expectedPath is not null && !PathsEqual(expectedPath, actualPath))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.PostconditionFailed,
                $"TIA Portal returned project '{actualPath}' while '{expectedPath}' was required. "
                + "The returned handle was not selected; inspect the TIA Portal UI before retrying.");
        }

        if (_activeContext?.Owner is LocalSessionOwner previousOwner
            && context.Owner is LocalSessionOwner nextOwner
            && PathsEqual(_selectedProjectPath, actualPath)
            && object.Equals(previousOwner.LocalSession, nextOwner.LocalSession)
            && object.Equals(previousOwner.Project, nextOwner.Project))
            return;

        var handleChanged = !object.Equals(EngineeringRoot, context.EngineeringRoot);
        SetActiveContext(context);

        if (!PathsEqual(_selectedProjectPath, actualPath))
        {
            _selectedProjectPath = actualPath;
            if (!handleChanged)
            {
                IncrementGeneration();
            }
        }
    }

    private static string? TryReadProjectPathForSelection(ProjectBase project)
    {
        try
        {
            return project.Path?.FullName;
        }
        catch (EngineeringException ex)
        {
            Console.Error.WriteLine($"Could not read a candidate project path: {ex.Message}");
            return null;
        }
    }

    private void SetActiveContext(ActiveProjectContext? context)
    {
        var rootChanged = !object.Equals(EngineeringRoot, context?.EngineeringRoot)
            || _activeContext?.Owner is LocalSessionOwner left && context?.Owner is LocalSessionOwner right
               && !object.Equals(left.LocalSession, right.LocalSession);
        _activeContext = context;
        if (rootChanged)
        {
            IncrementGeneration();
            // Known zero counts only for a root this worker just opened (V21 textual-interface pitfall).
            global::TiaMcpServer.OpennessWorker.Openness.Block.BlockExportLedger.Shared.Reset(context?.Owner.OpenedByWorker == true);
        }
    }

    private void SetPortalHandle(TiaPortal? portal, int? processId)
    {
        lock (_portalHandleSync)
        {
            if (ReferenceEquals(_tiaPortal, portal) && _attachedProcessId == processId)
            {
                return;
            }

            _tiaPortal = portal;
            _attachedProcessId = processId;
            _portalAttachmentRevision++;
            IncrementGeneration();
        }
    }

    private static bool PathsEqual(string? left, string? right)
    {
        var canonicalLeft = ProjectPathNormalization.Canonicalize(left);
        var canonicalRight = ProjectPathNormalization.Canonicalize(right);
        return canonicalLeft is null && canonicalRight is null
            || canonicalLeft is not null
            && canonicalRight is not null
            && string.Equals(canonicalLeft, canonicalRight, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatProcessId(int? processId)
        => processId?.ToString() ?? "(none)";

    private void IncrementGeneration()
        => Interlocked.Increment(ref _sessionGeneration);

    private void OnNotification(object? sender, NotificationEventArgs e)
    {
        // K75: a modal information message (e.g. "Export erfolgreich abgeschlossen.") blocked the Openness call until a human
        // clicked OK. Every notification is handled (IsHandled = true) in every access mode: it asks nothing, it only informs.
        PortalNotificationInfo info;
        try
        {
            info = PortalNotificationHandling.Handle(e);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Handling a TIA Portal notification failed: {ex.GetType().Name}");
            return;
        }

        lock (_notificationSync)
        {
            if (_requestRunning)
            {
                if (_requestNotifications.Count < PortalNotificationHandling.MaxRecorded) _requestNotifications.Add(info);
                // stderr is the request's warning buffer while a request runs.
                Console.Error.WriteLine(PortalNotificationHandling.RequestWarning(info));
                return;
            }

            _lateNotifications++;
            if (_lateNotificationTexts.Count < MaxReportedConfirmations) _lateNotificationTexts.Add(PortalNotificationHandling.Describe(info));
            Console.Error.WriteLine($"TIA Portal notification outside any MCP call: {PortalNotificationHandling.Describe(info)}; "
                + (info.Handled ? "handled (IsHandled = true)." : "IsHandled could not be set."));
        }
    }

    /// <summary>
    /// Starts recording the notifications of the request that is starting (K75) and returns one warning line for the notifications
    /// handled since the previous request ended (none: empty), like late confirmations. Thread-safe.
    /// </summary>
    internal IReadOnlyList<string> BeginRequestNotifications()
    {
        lock (_notificationSync)
        {
            _requestRunning = true;
            _requestNotifications.Clear();
            if (_lateNotifications == 0) return Array.Empty<string>();
            var line = $"{_lateNotifications} TIA Portal notification(s) arrived while no MCP call was running and were handled "
                + $"(IsHandled = true; {string.Join("; ", _lateNotificationTexts)}{(_lateNotifications > _lateNotificationTexts.Count ? "; ..." : string.Empty)}). "
                + "If you did not cause them, they may come from what the user did in TIA Portal's own window.";
            _lateNotifications = 0;
            _lateNotificationTexts.Clear();
            return new[] { line };
        }
    }

    /// <summary>The notifications handled since the current request started (at most <see cref="PortalNotificationHandling.MaxRecorded"/>).</summary>
    internal IReadOnlyList<PortalNotificationInfo> RequestNotifications()
    {
        lock (_notificationSync) return _requestNotifications.ToList();
    }

    /// <summary>Ends the request's recording; later notifications are reported to the next request.</summary>
    internal void EndRequestNotifications()
    {
        lock (_notificationSync) _requestRunning = false;
    }

    private void OnConfirmation(object? sender, ConfirmationEventArgs e)
    {
        // The scope observer runs under the lock, so disposing the scope waits for an in-flight
        // recording: once Dispose returns, every confirmation answered in the scope has been recorded.
        lock (_confirmationSync)
        {
            var scope = _confirmationScope;
            if (scope is not null)
            {
                // Answer first, so the dialog gets the scope's answer even if recording it fails.
                e.Result = scope.Answer;
                if (scope.Lingering)
                {
                    // After the call's evidence was collected: keep the call's answer (never the session-wide Yes
                    // under decline), record it as late, and log it now (between requests stderr reaches the host log).
                    var lateText = DescribeConfirmation(e);
                    scope.RecordLate(lateText);
                    Console.Error.WriteLine($"Late TIA Portal confirmation after {scope.Owner ?? "the previous call"} returned: "
                        + $"{lateText} answered {scope.Answer} by that call's policy.");
                    return;
                }

                try
                {
                    scope.Observe(e, scope.Answer);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Recording a scoped TIA confirmation failed: {ex.GetType().Name}");
                }

                return;
            }

            // No call has a scope open: the session-wide policy answers. TIA may raise such a dialog for something
            // the user does in its own window (K42; not verified whether V21 routes those to an attached client),
            // so it is answered as before but never silently: logged now and reported to the next request.
            var answer = _allowTiaConfirmations
                ? ConfirmationResult.Yes
                : ConfirmationResult.No;
            e.Result = answer;
            var text = DescribeConfirmation(e);
            _unscopedConfirmations++;
            _unscopedAnswerYes = answer == ConfirmationResult.Yes;
            if (_unscopedConfirmationTexts.Count < MaxReportedConfirmations) _unscopedConfirmationTexts.Add(text);
            Console.Error.WriteLine($"TIA Portal confirmation outside any MCP call: {text} answered {answer} by the session-wide policy.");
        }
    }

    private const int MaxReportedConfirmations = 10;

    /// <summary>
    /// The dialog text of a TIA Portal confirmation, read by reflection from a public readable
    /// <c>Text</c> string property. The Openness system manual (05/2021) lists <c>Text</c> among the attributes of
    /// confirmations; the member is not declared in the V21 reference stubs and is unverified for V21, so this is
    /// null when it is absent or unreadable.
    /// </summary>
    internal static string? ReadConfirmationText(object eventArgs)
    {
        try
        {
            var property = eventArgs.GetType().GetProperty("Text", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            return property is { CanRead: true } && property.PropertyType == typeof(string) && property.GetIndexParameters().Length == 0
                ? property.GetValue(eventArgs, null) as string
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The event type and, when readable, the dialog text (<see cref="ReadConfirmationText"/>), shortened for a warning line.
    /// </summary>
    internal static string DescribeConfirmation(object e)
    {
        var type = e.GetType().FullName ?? "(unknown)";
        var text = ReadConfirmationText(e);
        if (string.IsNullOrWhiteSpace(text)) return type;
        var flat = string.Join(" ", text!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (flat.Length > 200) flat = flat.Substring(0, 200) + "...";
        return $"{type} \"{flat}\"";
    }

    /// <summary>
    /// Until the returned scope is disposed, every TIA Portal <c>Confirmation</c> is answered
    /// <c>No</c> in every access mode (instead of the session-wide policy) and passed to
    /// <paramref name="observe"/> afterwards. <c>plc_download</c>'s default policy.
    /// </summary>
    internal IDisposable DeclineConfirmations(Action<ConfirmationEventArgs> observe)
    {
        if (observe is null) throw new ArgumentNullException(nameof(observe));
        return AnswerConfirmations(accept: false, (args, _) => observe(args));
    }

    /// <summary>
    /// Until the returned scope is disposed, every TIA Portal <c>Confirmation</c> gets one answer
    /// (instead of the session-wide policy) and is then passed to <paramref name="observe"/> with
    /// that answer. The answer is <c>Yes</c> only when <paramref name="accept"/> is true and this
    /// worker's access mode allows TIA confirmations (never in read-only); otherwise <c>No</c>. Used by
    /// <c>plc_download</c>, <c>plc_tia_online</c>, <c>plc_station_upload</c> (<c>portalConfirmations</c>) and
    /// <c>compare_to_online</c> (always No). Scopes do not nest.
    /// Disposing does not restore the session-wide policy at once: the scope lingers with the same answer
    /// (so a dialog V21 raises after an early return of the call is never accepted under decline), records
    /// such dialogs as late instead of observing them, and ends at <see cref="EndLingeringConfirmations"/>,
    /// which the worker calls when the next request starts; a new scope also ends a lingering one. Disposing
    /// waits for an in-flight observation, so the evidence collected before it is complete.
    /// </summary>
    internal IDisposable AnswerConfirmations(bool accept, Action<ConfirmationEventArgs, ConfirmationResult> observe, string? owner = null)
    {
        if (observe is null) throw new ArgumentNullException(nameof(observe));
        var scope = new ConfirmationScope(this,
            accept && WorkerOperationAuthorization.AllowsTiaConfirmations(AccessMode) ? ConfirmationResult.Yes : ConfirmationResult.No,
            observe, owner);
        lock (_confirmationSync)
        {
            if (_confirmationScope is { Lingering: false })
                throw new InvalidOperationException("A TIA confirmation-answering scope is already open.");
            if (_confirmationScope is { } lingering && lingering.LateSummary() is { } summary)
                _endedLateConfirmations.Add(summary);
            _confirmationScope = scope;
        }

        return scope;
    }

    /// <summary>
    /// Ends a lingering confirmation scope (the session-wide policy applies again) and returns one warning
    /// line per scope that answered late dialogs, plus one line for dialogs the session-wide policy answered
    /// since the last request (no scope open), for the response of the request that is starting.
    /// Thread-safe; idempotent. The worker calls it before every request, so a scope never outlives the
    /// request after its own.
    /// </summary>
    internal IReadOnlyList<string> EndLingeringConfirmations()
    {
        lock (_confirmationSync)
        {
            var lines = _endedLateConfirmations.ToList();
            _endedLateConfirmations.Clear();
            if (_confirmationScope is { Lingering: true } lingering)
            {
                if (lingering.LateSummary() is { } summary) lines.Add(summary);
                _confirmationScope = null;
            }

            if (_unscopedConfirmations > 0)
            {
                lines.Add($"{_unscopedConfirmations} TIA Portal confirmation dialog(s) arrived while no MCP call was answering dialogs "
                    + $"and were answered {(_unscopedAnswerYes ? "Yes" : "No")} by the session-wide policy ({string.Join(", ", _unscopedConfirmationTexts)}"
                    + $"{(_unscopedConfirmations > _unscopedConfirmationTexts.Count ? ", ..." : string.Empty)}). "
                    + "If you did not cause them, they may come from what the user did in TIA Portal's own window; check the project.");
                _unscopedConfirmations = 0;
                _unscopedConfirmationTexts.Clear();
            }

            return lines;
        }
    }

    private sealed class ConfirmationScope : IDisposable
    {
        private const int MaxLateTypes = MaxReportedConfirmations;

        private readonly TiaPortalSession _session;
        private readonly List<string> _lateTypes = new();
        private int _late;

        public ConfirmationScope(TiaPortalSession session, ConfirmationResult answer, Action<ConfirmationEventArgs, ConfirmationResult> observe,
            string? owner)
        {
            _session = session;
            Answer = answer;
            Observe = observe;
            Owner = owner;
        }

        public ConfirmationResult Answer { get; }

        public Action<ConfirmationEventArgs, ConfirmationResult> Observe { get; }

        public string? Owner { get; }

        /// <summary>Set (under the session's lock) when the call disposed the scope; it then answers and records late dialogs.</summary>
        public bool Lingering { get; private set; }

        /// <summary>Called under the session's lock.</summary>
        public void RecordLate(string? type)
        {
            _late++;
            if (_lateTypes.Count < MaxLateTypes) _lateTypes.Add(string.IsNullOrWhiteSpace(type) ? "(unknown)" : type!);
        }

        /// <summary>Called under the session's lock; null when no late dialog was answered.</summary>
        public string? LateSummary()
            => _late == 0 ? null
                : $"{_late} TIA Portal confirmation dialog(s) were raised after {Owner ?? "the previous call"} returned and were answered {Answer} "
                    + $"by its portalConfirmations policy ({string.Join(", ", _lateTypes)}{(_late > _lateTypes.Count ? ", ..." : string.Empty)}); "
                    + "they are not in that call's evidence. Inspect the PLC and the project.";

        public void Dispose()
        {
            lock (_session._confirmationSync)
            {
                if (ReferenceEquals(_session._confirmationScope, this))
                    Lingering = true;
            }
        }
    }

    private void SubscribeToPortal(TiaPortal portal)
    {
        EventHandler<EventArgs>? handler = null;
        handler = (_, _) =>
        {
            lock (_portalHandleSync)
            {
                if (ReferenceEquals(_portalDisposedHandler, handler)
                    && ClearPortal(portal, unexpectedDisposal: true))
                    Console.Error.WriteLine("Attached TIA Portal instance was disposed.");
            }
        };
        lock (_portalHandleSync)
            _portalDisposedHandler = handler;
        portal.Notification += OnNotification;
        portal.Confirmation += OnConfirmation;
        portal.Disposed += handler;
    }

    private bool ClearPortal(TiaPortal? portal, bool unexpectedDisposal)
    {
        lock (_portalHandleSync)
        {
            if (portal is null || !ReferenceEquals(portal, _tiaPortal))
                return false;

            if (unexpectedDisposal)
                _portalDisposalRevision++;
            _portalDisposedHandler = null;
            SetActiveContext(null);
            _selectedProjectPath = null;
            SetPortalHandle(null, null);
            return true;
        }
    }

    public void Disconnect()
    {
        if (_tiaPortal is not null)
        {
            var refusal = EvaluatePortalDetach(_tiaPortal);
            if (refusal is not null)
                throw new WorkerOperationException(WorkerFailureCategories.GuardBlocked, refusal);
        }

        ReleaseTemporaryTargetIfSafe();

        var portal = _tiaPortal;
        var disposedHandler = _portalDisposedHandler;
        // Forget the attachment before unsubscribing so an already queued callback is stale.
        ClearPortal(portal, unexpectedDisposal: false);
        if (portal != null)
        {
            portal.Notification -= OnNotification;
            portal.Confirmation -= OnConfirmation;
            portal.Disposed -= disposedHandler;
        }

        SetActiveContext(null);
        _selectedProjectPath = null;
        portal?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Disconnect();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TiaPortalSession));
        }
    }
}
