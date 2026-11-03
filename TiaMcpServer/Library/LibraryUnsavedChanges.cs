using System.Runtime.CompilerServices;
using System.Text.Json;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Library;

/// <summary>
/// The global libraries this server changed through <c>library_write</c> and has not saved or closed since.
/// The documented close is the parameterless <c>Close()</c>: it offers no save-or-discard choice, and no documented
/// member reports a global library's unsaved changes. So close_global_library is refused by the block guard
/// <see cref="LibraryGuardDefinitions.CloseUnsavedChanges"/> while this ledger (or an earlier item of the same call)
/// holds a change of that library that no later save_global_library covers. The ledger is the host's own bookkeeping:
/// one per worker client (it survives worker restarts), keyed by the TIA Portal process the change was made in
/// (<see cref="TiaMcpServer.Contracts.Project.ProjectBindingSnapshot.PortalProcessId"/> of the pinned binding: global
/// libraries belong to a Portal session, and the same file can be open in two Portals) and the normalized library path.
/// A change recorded without a known process matches every process, and a save, close or fresh open without a known
/// process forgets nothing (conservative: the close stays refused rather than wrongly allowed). Changes made in the
/// TIA Portal UI, by another Openness client or before the server started are not known to it.
/// </summary>
public sealed class LibraryUnsavedChanges
{
    /// <summary>Changes listed per library; later ones are counted only.</summary>
    internal const int MaxListedChanges = 10;

    private static readonly ConditionalWeakTable<OpennessWorkerClient, LibraryUnsavedChanges> PerClient = new();

    private readonly object _sync = new();
    private readonly Dictionary<(int? Portal, string Key), Entry> _entries = new();

    private sealed class Entry
    {
        public string Library { get; init; } = string.Empty;

        public List<string> Changes { get; } = new();

        public int Count { get; set; }
    }

    /// <summary>One known unsaved library: its selector as the caller wrote it first, the listed changes and their total.</summary>
    public sealed record Pending(string Library, IReadOnlyList<string> Changes, int Count);

    /// <summary>The ledger of the server's worker client.</summary>
    public static LibraryUnsavedChanges For(OpennessWorkerClient client)
        => PerClient.GetValue(client ?? throw new ArgumentNullException(nameof(client)), _ => new LibraryUnsavedChanges());

    /// <summary>
    /// The known unsaved libraries by key (<see cref="LibraryCatalog.Key"/>) that a call in the TIA Portal process
    /// <paramref name="portalProcessId"/> must respect: that process's entries and the entries recorded without a known
    /// process. With an unknown process, every entry (the library may belong to any of them). Entries of one key are merged.
    /// </summary>
    public IReadOnlyDictionary<string, Pending> Snapshot(int? portalProcessId)
    {
        lock (_sync)
        {
            var merged = new Dictionary<string, Pending>(StringComparer.Ordinal);
            foreach (var ((portal, key), entry) in _entries.OrderBy(pair => pair.Key.Portal is null ? 1 : 0))
            {
                if (portalProcessId is not null && portal is not null && portal != portalProcessId) continue;
                merged[key] = merged.TryGetValue(key, out var earlier)
                    ? new Pending(earlier.Library, earlier.Changes.Concat(entry.Changes).Take(MaxListedChanges).ToArray(), earlier.Count + entry.Count)
                    : new Pending(entry.Library, entry.Changes.ToArray(), entry.Count);
            }

            return merged;
        }
    }

    /// <summary>
    /// The global libraries an item changes (by key, with the selector as written): the <c>library</c> of a content item
    /// that edits library content, and the <c>targetLibrary</c> of copy_master_copy. Instantiations and update_project change
    /// the project (and the project library), not the global library they read.
    /// </summary>
    internal static IReadOnlyList<(string Key, string Library)> ChangedGlobalLibraries(LibraryOperationRequest item)
    {
        var changed = new List<(string, string)>();
        if (item.Operation is LibraryMethods.CreateMasterCopy or LibraryMethods.DeleteMasterCopy or LibraryMethods.RenameMasterCopy
                or LibraryMethods.RenameType or LibraryMethods.RenameTypeFolder or LibraryMethods.EditTypeVersion
                or LibraryMethods.ReleaseTypeVersion or LibraryMethods.DiscardTypeVersion or LibraryMethods.SetDefaultTypeVersion
                or LibraryMethods.DeleteTypeVersion or LibraryMethods.CleanUpLibrary or LibraryMethods.DeleteType
                or LibraryMethods.SetTypeDoNotUse or LibraryMethods.CreateTypeFolder or LibraryMethods.DeleteTypeFolder
                or LibraryMethods.CreateMasterCopyFolder or LibraryMethods.DeleteMasterCopyFolder or LibraryMethods.RenameMasterCopyFolder
                or LibraryMethods.CreateTypeFromDocuments or LibraryMethods.CreateVersionFromDocuments
            && item.Library is { } library && !LibraryPolicy.IsProject(library))
            changed.Add((LibraryCatalog.Key(library)!, library));
        if (item.Operation is LibraryMethods.CopyMasterCopy or LibraryMethods.UpdateLibrary && item.TargetLibrary is { } target && !LibraryPolicy.IsProject(target))
            changed.Add((LibraryCatalog.Key(target)!, target));
        return changed;
    }

    /// <summary>
    /// Failure categories after which the worker has not changed anything: refusals before the Siemens call. Not
    /// <c>engineering_rejected</c>: the mutation flag spans the whole worker method, so that category does not prove that no
    /// setter or create ran (AGENTS, failure categories). Every other failure may follow a change (a read-back, a declined
    /// confirmation, a lost answer, a Portal rejection after a partial change) and is recorded as one.
    /// </summary>
    private static readonly HashSet<string> NothingChanged = new(StringComparer.Ordinal)
    {
        WorkerFailureCategories.ValidationError, WorkerFailureCategories.BindingConflict, WorkerFailureCategories.AccessDenied,
        WorkerFailureCategories.StateChanged, WorkerFailureCategories.TargetNotFound, WorkerFailureCategories.TargetAmbiguous,
        WorkerFailureCategories.TargetKindUnsupported, WorkerFailureCategories.UnsupportedCapability,
    };

    /// <summary>
    /// Updates the ledger from one mutated item's worker result and projected outcome, made in the TIA Portal process
    /// <paramref name="portalProcessId"/> (null: unknown; then nothing is forgotten and changes match every process).
    /// </summary>
    public void Observe(int? portalProcessId, LibraryOperationRequest item, WorkerCallResult result, StructuredOperationItem outcome)
    {
        var succeeded = outcome.Status == OperationBatchStatus.Succeeded;
        lock (_sync)
        {
            switch (item.Operation)
            {
                case LibraryMethods.SaveGlobalLibrary when succeeded:
                case LibraryMethods.CloseGlobalLibrary when succeeded:
                    Forget(portalProcessId, LibraryCatalog.Key(item.Library)!);
                    return;
                case LibraryMethods.OpenGlobalLibrary or LibraryMethods.RetrieveGlobalLibrary or LibraryMethods.CreateGlobalLibrary when succeeded:
                    // A library this call opened has no unsaved change of this server (an entry left from a library closed elsewhere is
                    // stale). One that was already open (reported alreadyOpen, also when Open returned it) keeps its entry.
                    if (Opened(outcome) is { } opened) Forget(portalProcessId, opened);
                    return;
            }

            // A failure detected after the worker answered (binding or identity drift) followed the mutation whatever its category.
            if (!succeeded && !result.IsPostOperationFailure && (outcome.Failure is null || NothingChanged.Contains(outcome.Failure.Category))) return;
            foreach (var (changed, library) in ChangedGlobalLibraries(item))
            {
                var key = (portalProcessId, changed);
                if (!_entries.TryGetValue(key, out var entry)) _entries[key] = entry = new Entry { Library = library };
                entry.Count++;
                if (entry.Changes.Count < MaxListedChanges)
                    entry.Changes.Add($"{item.Operation} ({item.OperationId}{(succeeded ? string.Empty : ", failed after a possible change")})");
            }
        }
    }

    /// <summary>Forgets the library's entry of a known process; with an unknown process nothing (it may be another process's).</summary>
    private void Forget(int? portalProcessId, string key)
    {
        if (portalProcessId is not null) _entries.Remove((portalProcessId, key));
    }

    /// <summary>The key of the library an open, retrieve or create item opened; null when it was already open or unreadable.</summary>
    private static string? Opened(StructuredOperationItem outcome)
    {
        if (outcome.Result is not { ValueKind: JsonValueKind.Object } result) return null;
        if (result.TryGetProperty("alreadyOpen", out var alreadyOpen) && alreadyOpen.ValueKind == JsonValueKind.True) return null;
        return result.TryGetProperty("library", out var library) && library.ValueKind == JsonValueKind.Object
            && library.TryGetProperty("library", out var selector) && selector.ValueKind == JsonValueKind.String
                ? LibraryCatalog.Key(selector.GetString())
                : null;
    }
}
