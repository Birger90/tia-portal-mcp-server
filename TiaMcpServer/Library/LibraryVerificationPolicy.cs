using System.Text.Json;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Library;

/// <summary>
/// Completed library writes are <c>trusted</c> or <c>superseded</c>: the worker mutation itself reads back what the
/// sources make readable (rename read-back, delete/close absence, default flag, the state and number of a released
/// version, the in-work state after edit, a discarded version's absence or state change, the object CreateFrom returned)
/// and fails <c>postcondition_failed</c> otherwise, so there is no separate verification read. When the worker could not
/// read a postcondition back (its result carries <c>unverified</c>), the item's entry is <see cref="NotVerified"/> with
/// success false (<see cref="WithWorkerEvidence"/>). Keys: the library, then the addressed object.
/// K61 (live V21 2026-11-02: these said "trusted, nothing was read back" although library_read can read the result): the
/// library lifecycle and master copy items are read back afterwards with the reads library_read uses
/// (<see cref="ReadBackAsync"/>); save_global_library stays trusted because no documented read shows a saved state.
/// </summary>
public sealed class LibraryVerificationPolicy(IReadOnlyList<LibraryOperationRequest>? items = null,
    Func<string, LibraryRequestInfo, Task<WorkerCallResult>>? call = null, IReadOnlyDictionary<string, JsonElement>? results = null,
    List<string>? readWarnings = null)
    : IWriteVerificationPolicy<LibraryOperationRequest>
{
    /// <summary>The check of a library lifecycle read-back: the library is (or, after a close, is not) listed by list_libraries.</summary>
    public const string OpenLibrariesCheck = "openLibraries";

    /// <summary>The check of a master copy read-back: the root master copy folder of the library (get_library_tree masterCopies).</summary>
    public const string MasterCopyCheck = "masterCopy";

    /// <summary>The prefix of the keys a content item owns when the call saves its library before closing it.</summary>
    private const string SavedPrefix = "saved\n";

    /// <summary>The prefix of the key a close owns and a later open, retrieve or create of the same library offers.</summary>
    private const string LifecyclePrefix = "lifecycle\n";

    /// <summary>The check of a completed item whose postcondition the worker could not read back (vci_write's word).</summary>
    public const string NotVerified = "notVerified";

    public string ReadTool => LibraryMethods.ReadTool;

    /// <summary>
    /// Replaces the <c>trusted</c> entry of every succeeded item whose worker result says <c>unverified</c> by a failing
    /// <see cref="NotVerified"/> entry with the worker's reason; superseded entries stay (a later item covers the object).
    /// </summary>
    public static WriteVerification WithWorkerEvidence(WriteVerification verification, StructuredOperationBatch batch)
    {
        var reasons = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in batch.Operations)
            if (item.Status == OperationBatchStatus.Succeeded && item.Result is { ValueKind: JsonValueKind.Object } result
                && result.TryGetProperty("unverified", out var reason) && reason.ValueKind == JsonValueKind.String)
                reasons[item.OperationId] = reason.GetString()!;
        if (reasons.Count == 0) return verification;
        var entries = verification.Operations.Select(entry => entry.Check == WriteVerificationChecks.Trusted && reasons.TryGetValue(entry.OperationId, out var why)
                ? new WriteOperationVerification(entry.OperationId, false, NotVerified, null, null,
                    $"Operation '{entry.OperationId}': the Openness call completed, but its result could not be read back: {why} "
                    + $"Inspect it with {LibraryMethods.ReadTool} before relying on it.")
                : entry)
            .ToArray();
        return new WriteVerification(entries.All(entry => entry.Success), entries, verification.Omission);
    }

    public IReadOnlyList<string> ObjectKeys(LibraryOperationRequest item, bool asLater)
    {
        var library = LibraryCatalog.LibraryKey(item);
        switch (item.Operation)
        {
            case LibraryMethods.SaveGlobalLibrary:
                // K47 (live 2026-11-02): a later close of the same library superseded the save, but closing does not undo a
                // save. The save owns a key outside the library's namespace, so no later item supersedes it, and it changes no
                // object an earlier item addressed.
                return asLater || library is null ? Array.Empty<string>() : new[] { "save\n" + library };
            case LibraryMethods.SetPortalSetting:
                return new[] { "portal-setting\n" + item.SettingsFolderPath?.Trim('/').ToUpperInvariant() + "\n" + item.SettingName?.ToUpperInvariant() };
            case LibraryMethods.OpenSecondaryProject or LibraryMethods.RetrieveSecondaryProject:
                return asLater || library is null ? Array.Empty<string>() : new[] { library };
            case LibraryMethods.CloseSecondaryProject:
                return library is null ? Array.Empty<string>() : new[] { library };
            case LibraryMethods.OpenGlobalLibrary or LibraryMethods.RetrieveGlobalLibrary or LibraryMethods.CreateGlobalLibrary:
                // Opening changes no object an earlier item addressed; it only undoes an earlier close of the same library,
                // whose "not open" read-back it supersedes (K61).
                return library is null ? Array.Empty<string>() : new[] { asLater ? LifecyclePrefix + library : library };
            case LibraryMethods.CloseGlobalLibrary:
                return library is null ? Array.Empty<string>() : asLater ? new[] { library } : new[] { library, LifecyclePrefix + library };
            case LibraryMethods.InstantiateMasterCopy or LibraryMethods.InstantiateTypeVersion or LibraryMethods.UpdateProject or LibraryMethods.HarmonizeProject:
                return new[] { "project-content\n" + Target(item) };
            case LibraryMethods.ArchiveGlobalLibrary or LibraryMethods.SaveGlobalLibraryAs:
                // Like a save: the archive or the copy is written outside the library; a later close does not undo it.
                return asLater || library is null ? Array.Empty<string>() : new[] { item.Operation + "\n" + library + "\n" + item.TargetDirectory?.ToUpperInvariant() };
            case LibraryMethods.ExportTypeVersion or LibraryMethods.ExportTypeVersionAsDocuments:
                // A file export changes no library object; it owns its files only.
                return new[] { "export\n" + (item.FilePath ?? item.DocumentDirectory + "\\" + item.DocumentName)?.ToUpperInvariant() };
            case LibraryMethods.UpdateLibrary when LibraryCatalog.Key(item.TargetLibrary) is { } updated:
            {
                // UpdateLibrary changes the target library's types, not the source library.
                var updateKeys = new[] { updated + "\nupdated" };
                return asLater ? updateKeys.Concat(updateKeys.Select(key => SavedPrefix + key)).ToArray()
                    : SavedBeforeClose(item, updated) ? updateKeys.Select(key => SavedPrefix + key).ToArray() : updateKeys;
            }
        }

        if (library is null) return Array.Empty<string>();
        // K47 (live 2026-11-02): copy_master_copy (project library -> global library) owned the source master copy's key, so a
        // later delete of the same name in the project library (another object) superseded it. A copy leaves the source as it
        // is: it owns, and as a later item offers, the copy in the target library.
        if (item.Operation == LibraryMethods.CopyMasterCopy && LibraryCatalog.Key(item.TargetLibrary) is { } copyTarget && item.MasterCopyName is not null)
        {
            var copyKeys = new[] { copyTarget + "\nmasterCopy:" + item.MasterCopyName.ToUpperInvariant(), copyTarget + "\nmasterCopies" };
            return asLater ? copyKeys.Concat(copyKeys.Select(key => SavedPrefix + key)).ToArray()
                : SavedBeforeClose(item, copyTarget) ? copyKeys.Select(key => SavedPrefix + key).ToArray() : copyKeys;
        }
        var keys = ContentKeys(item, library);
        if (asLater) return keys.Concat(keys.Select(key => SavedPrefix + key)).ToArray();
        // K47: a close supersedes the content items of its library (it may discard them), but not those a save of that
        // library between them persisted; those own their keys in the saved namespace, which only later content items offer.
        return SavedBeforeClose(item, library) ? keys.Select(key => SavedPrefix + key).ToArray() : keys;
    }

    /// <summary>True when the first later save or close of <paramref name="library"/> in the call is a save (for a copy, the target library).</summary>
    private bool SavedBeforeClose(LibraryOperationRequest item, string library)
    {
        if (items is null) return false;
        var index = items.ToList().FindIndex(candidate => candidate.OperationId == item.OperationId);
        if (index < 0) return false;
        foreach (var later in items.Skip(index + 1))
        {
            if (later.Operation is not (LibraryMethods.SaveGlobalLibrary or LibraryMethods.CloseGlobalLibrary)) continue;
            if (LibraryCatalog.LibraryKey(later) != library) continue;
            return later.Operation == LibraryMethods.SaveGlobalLibrary;
        }

        return false;
    }

    private static List<string> ContentKeys(LibraryOperationRequest item, string library)
    {
        var keys = new List<string>();
        if (item.Operation is LibraryMethods.CreateMasterCopyFolder or LibraryMethods.DeleteMasterCopyFolder or LibraryMethods.RenameMasterCopyFolder)
        {
            var folder = (item.MasterCopyFolderPath ?? "/").ToUpperInvariant();
            keys.Add(library + "\nmasterCopyFolder:" + (item.Operation == LibraryMethods.CreateMasterCopyFolder ? folder + "/" + item.NewName?.ToUpperInvariant() : folder));
            return keys;
        }

        if (item.Operation == LibraryMethods.CreateTypeFolder)
        {
            keys.Add(library + "\nfolder:" + (item.FolderPath ?? "/").ToUpperInvariant() + "/" + item.NewName?.ToUpperInvariant());
            return keys;
        }

        if (item.MasterCopyName is not null)
        {
            keys.Add(library + "\nmasterCopy:" + item.MasterCopyName.ToUpperInvariant());
            if (item.Operation == LibraryMethods.RenameMasterCopy && item.NewName is not null)
                keys.Add(library + "\nmasterCopy:" + item.NewName.ToUpperInvariant());
        }
        else if (item.TypePath is not null || item.TypeGuid is not null || item.VersionGuid is not null)
            keys.Add(library + "\ntype:" + (item.TypePath ?? item.TypeGuid ?? "version:" + item.VersionGuid)!.ToUpperInvariant());
        else if (item.FolderPath is not null)
            keys.Add(library + "\nfolder:" + item.FolderPath.ToUpperInvariant());
        else if (item.Operation is LibraryMethods.CreateMasterCopy or LibraryMethods.CreateTypeFromDocuments)
            keys.Add(library + (item.Operation == LibraryMethods.CreateMasterCopy ? "\nmasterCopies" : "\ntypes"));
        if (item.Operation == LibraryMethods.CopyMasterCopy && LibraryCatalog.Key(item.TargetLibrary) is { } target)
            keys.Add(target + "\nmasterCopies");
        return keys;
    }

    private static string Target(LibraryOperationRequest item)
        => item.Target is { } target ? $"{target.Kind}:{target.PlcName}:{target.FolderPath}:{target.Parent?.DeviceName}".ToUpperInvariant()
            : "plc:" + item.PlcName?.ToUpperInvariant();

    /// <summary>The items read back after the call (K61); every other item is verified inside the worker mutation (trusted).</summary>
    public bool HasReadBack(LibraryOperationRequest item)
        => call is not null && item.Operation is LibraryMethods.OpenGlobalLibrary or LibraryMethods.RetrieveGlobalLibrary
            or LibraryMethods.CreateGlobalLibrary or LibraryMethods.CloseGlobalLibrary
            or LibraryMethods.CreateMasterCopy or LibraryMethods.CopyMasterCopy or LibraryMethods.DeleteMasterCopy;

    /// <summary>A save has no read-back: no documented member reports whether a global library has unsaved changes.</summary>
    public string? WorkerCheckEvidence(LibraryOperationRequest item)
        => item.Operation == LibraryMethods.SaveGlobalLibrary
            ? "a save is not read back: no documented Openness member reports whether a global library has unsaved changes, so nothing observable shows the saved state."
            : null;

    /// <summary>
    /// A close that supersedes a content item of its library (no save between them) is read back only as "no longer open":
    /// the content item's own change is not verified there, and the close may have discarded it.
    /// </summary>
    public string? SupersessionEvidence(LibraryOperationRequest item, LibraryOperationRequest later)
        => later.Operation == LibraryMethods.CloseGlobalLibrary && !LibraryMethods.IsLifecycle(item.Operation)
            ? "the later close was read back only as no longer open; no save of the library came between them, so the close may have discarded this change, and it is not verified."
            : null;

    public async Task<WriteOperationVerification> ReadBackAsync(LibraryOperationRequest item, StructuredOperationFailure? failure)
    {
        var written = Written(item);
        switch (item.Operation)
        {
            case LibraryMethods.CloseGlobalLibrary:
                return await OpenLibrariesReadBackAsync(item, item.Library, expectOpen: false, failure).ConfigureAwait(false);
            case LibraryMethods.OpenGlobalLibrary:
                return await OpenLibrariesReadBackAsync(item, written?.Library?.Path ?? item.Path, expectOpen: true, failure).ConfigureAwait(false);
            case LibraryMethods.RetrieveGlobalLibrary or LibraryMethods.CreateGlobalLibrary:
                return await OpenLibrariesReadBackAsync(item, written?.Library?.Path, expectOpen: true, failure).ConfigureAwait(false);
            case LibraryMethods.CreateMasterCopy:
                return await MasterCopyReadBackAsync(item, item.Library,
                    written?.Created.Select(created => created.Name).FirstOrDefault(name => !string.IsNullOrEmpty(name))
                        ?? (failure is null ? null : item.Source?.Path.Split('/').LastOrDefault()), expectPresent: true, failure).ConfigureAwait(false);
            case LibraryMethods.CopyMasterCopy:
                return await MasterCopyReadBackAsync(item, item.TargetLibrary,
                    written?.Created.Select(created => created.Name).FirstOrDefault(name => !string.IsNullOrEmpty(name)) ?? item.MasterCopyName,
                    expectPresent: true, failure).ConfigureAwait(false);
            default:
                return await MasterCopyReadBackAsync(item, item.Library, item.MasterCopyName, expectPresent: false, failure).ConfigureAwait(false);
        }
    }

    private LibraryWriteResultInfo? Written(LibraryOperationRequest item)
        => results is not null && results.TryGetValue(item.OperationId, out var raw) && raw.ValueKind == JsonValueKind.Object
            ? CanonicalJson.Deserialize<LibraryWriteResultInfo>(raw.GetRawText())
            : null;

    private static WriteOperationVerification Entry(LibraryOperationRequest item, string check, bool success, string? expected, string? actual,
        StructuredOperationFailure? failure, string? message)
        => failure is not null
            ? new(item.OperationId, false, check, expected, actual,
                $"Operation '{item.OperationId}' failed with {failure.Category}; the state read afterwards is reported. Inspect it with {LibraryMethods.ReadTool} before retrying.")
            : new(item.OperationId, success, check, expected, actual, message);

    /// <summary>open/retrieve/create: the library is listed among the open libraries; close: it is not (list_libraries).</summary>
    private async Task<WriteOperationVerification> OpenLibrariesReadBackAsync(LibraryOperationRequest item, string? path, bool expectOpen,
        StructuredOperationFailure? failure)
    {
        var expected = path is null ? null : $"{path} {(expectOpen ? "open" : "not open")}";
        if (path is null)
            return Entry(item, OpenLibrariesCheck, false, null, null, failure,
                $"Operation '{item.OperationId}': the result names no library path, so the open libraries could not be checked. Inspect them with {LibraryMethods.ReadTool} list_libraries.");
        var (list, error) = await ReadAsync<LibraryListInfo>(item, LibraryMethods.ListLibraries, new LibraryRequestInfo { Operation = LibraryMethods.ListLibraries }).ConfigureAwait(false);
        if (list is null)
            return Entry(item, OpenLibrariesCheck, false, expected, null, failure,
                $"Operation '{item.OperationId}': the open libraries could not be re-read ({error}).");
        var listed = list.Libraries.Any(library => !LibraryPolicy.IsProject(library.Library)
            && (LibraryPolicy.SamePath(library.Path, path) || LibraryPolicy.SamePath(library.Library, path)));
        var actual = $"{path} {(listed ? "open" : "not open")}";
        var success = listed == expectOpen;
        return Entry(item, OpenLibrariesCheck, success, expected, actual, failure, success
            ? $"Operation '{item.OperationId}': list_libraries read afterwards {(expectOpen ? "lists" : "no longer lists")} the library."
            : $"Operation '{item.OperationId}': list_libraries read afterwards {(expectOpen ? "does not list" : "still lists")} the library. Inspect it with {LibraryMethods.ReadTool} list_libraries.");
    }

    /// <summary>create/copy: the master copy is in the library's root master copy folder; delete: it is not (get_library_tree masterCopies).</summary>
    private async Task<WriteOperationVerification> MasterCopyReadBackAsync(LibraryOperationRequest item, string? library, string? name, bool expectPresent,
        StructuredOperationFailure? failure)
    {
        var expected = library is null || name is null ? null : $"{library}: master copy '{name}' {(expectPresent ? "present" : "absent")}";
        if (library is null || name is null)
            return Entry(item, MasterCopyCheck, false, expected, null, failure,
                $"Operation '{item.OperationId}': the master copy's name or library is not known, so it could not be checked. Inspect it with {LibraryMethods.ReadTool} get_library_tree.");
        var names = new List<string>();
        for (var offset = 0; ; offset += LibraryPolicy.MaxPageLimit)
        {
            var (tree, error) = await ReadAsync<LibraryTreeInfo>(item, LibraryMethods.GetLibraryTree, new LibraryRequestInfo
            {
                Operation = LibraryMethods.GetLibraryTree, Library = library, Content = LibraryPolicy.TreeContents.MasterCopies,
                IncludeVersions = false, Offset = offset, Limit = LibraryPolicy.MaxPageLimit,
            }).ConfigureAwait(false);
            if (tree is null)
                return Entry(item, MasterCopyCheck, false, expected, null, failure,
                    $"Operation '{item.OperationId}': the master copies of {library} could not be re-read ({error}).");
            names.AddRange(tree.Entries.Where(entry => entry.Kind == LibraryPolicy.EntryKinds.MasterCopy).Select(entry => entry.Name ?? string.Empty));
            if (tree.Returned == 0 || offset + tree.Returned >= tree.Total) break;
        }

        // Master copy names are compared like the worker finds them in the root folder (exact).
        var present = names.Contains(name, StringComparer.Ordinal);
        var actual = $"{library}: master copy '{name}' {(present ? "present" : "absent")}";
        var success = present == expectPresent;
        return Entry(item, MasterCopyCheck, success, expected, actual, failure, success
            ? $"Operation '{item.OperationId}': get_library_tree read afterwards shows the root master copy folder as expected."
            : $"Operation '{item.OperationId}': get_library_tree read afterwards does not show the expected root master copy folder. Inspect it with {LibraryMethods.ReadTool} get_library_tree.");
    }

    private async Task<(T? Value, string? Error)> ReadAsync<T>(LibraryOperationRequest item, string operation, LibraryRequestInfo request) where T : class
    {
        var result = await call!(LibraryMethods.ForOperation(operation), request).ConfigureAwait(false);
        // A read-back response can carry warnings meant for the call (e.g. a TIA Portal dialog raised after the mutation returned).
        readWarnings?.AddRange(result.Warnings ?? Array.Empty<string>());
        var projected = LibraryPayloadContract.ProjectRead(new LibraryOperationRequest { OperationId = item.OperationId, Operation = operation }, result);
        return projected.Result is { } value
            ? (CanonicalJson.Deserialize<T>(value.GetRawText()), null)
            : (null, projected.Failure?.Message ?? "no result");
    }
}
