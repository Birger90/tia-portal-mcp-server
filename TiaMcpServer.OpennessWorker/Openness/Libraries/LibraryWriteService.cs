using System.Collections;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.Library;
using Siemens.Engineering.Library.MasterCopies;
using Siemens.Engineering.Library.Types;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Types;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.Network;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Openness.Project;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Libraries;

/// <summary>
/// Planning (<c>library_plan</c>, no mutation) and the <c>library_write</c> mutations (the issue #17 additions in LibraryWriteService.Extended.cs). Planning resolves
/// every target the mutation will use and reports consequences; the mutation resolves them again on fresh
/// state, calls the documented member and reads back what it can (renames, deletes, close, default version).
/// </summary>
internal static partial class LibraryWriteService
{
    // ================================================================ planning

    public static LibraryPlanInfo Plan(TiaPortal portal, ProjectBase project, LibraryRequestInfo request)
    {
        var plan = new LibraryPlanInfo { Operation = request.Operation };
        if (request.Operation == LibraryMethods.SetPortalSetting)
        {
            // A Portal-wide setting: no library is resolved. The plan names the current value and type.
            var current = PortalSettings.Current(portal, request);
            plan.Target = $"TIA Portal setting {current.FolderPath}/{current.Name} = {current.Value ?? "null"} ({current.ValueType ?? "unknown type"}) -> {request.SettingValue}";
            plan.Consequences.Add("The setting applies to this TIA Portal instance, not to the project; it is not saved with the project.");
            if (!current.Writable) plan.Consequences.Add("The live TiaPortalSetting.Value has no public setter; the write will be refused (unsupported_capability).");
            if (current.SecurityRelevant) plan.Consequences.Add("The setting is security-relevant by the names TIA Portal resolved (certificates, communication, encryption, passwords, trust, acceptance, access).");
            plan.Setting = current;
            return plan;
        }

        if (LibraryMethods.IsSecondaryProject(request.Operation))
        {
            PlanLifecycleExtended(portal, project, request, plan);
            return plan;
        }

        switch (request.Operation)
        {
            case LibraryMethods.OpenGlobalLibrary:
                RequireFile(request.Path, "path");
                plan.AlreadyOpen = LibraryResolver.FindOpenFile(LibraryResolver.OpenGlobals(portal, new List<string>()), request.Path!, "path") is not null;
                if (!plan.AlreadyOpen && InfoToOpen(portal, request) is { } byInfo)
                {
                    // K80: a system or corporate library is opened by its GlobalLibraryInfo, never by path.
                    plan.Target = $"{byInfo.LibraryType} global library {request.Path} ({GlobalLibraryInfoOpen.Call}, read-only)";
                    plan.Consequences.Add(GlobalLibraryInfoOpen.Consequence(byInfo.LibraryType));
                    break;
                }

                plan.Target = $"global library file {request.Path} ({(request.Upgrade ? "OpenWithUpgrade" : "OpenMode " + OpenMode(request))})";
                if (plan.AlreadyOpen) plan.Consequences.Add("The file is already open as a global library; nothing is called and its open mode is not changed.");
                else if (request.Upgrade) plan.Consequences.Add("OpenWithUpgrade may upgrade the library file in place on disk.");
                else if (OpenMode(request) == "ReadWrite") plan.Consequences.Add("Opened read-write; library edits persist only through save_global_library.");
                break;
            case LibraryMethods.RetrieveGlobalLibrary:
                RequireFile(request.Path, "path");
                plan.Target = $"archive {request.Path} into {request.TargetDirectory} ({(request.Upgrade ? "RetrieveWithUpgrade, " : string.Empty)}OpenMode {OpenMode(request)})";
                plan.Consequences.Add($"Retrieve extracts the archive into {request.TargetDirectory} and opens the extracted library.");
                if (Directory.Exists(request.TargetDirectory) && Directory.EnumerateFileSystemEntries(request.TargetDirectory!).Any())
                    plan.Consequences.Add($"The target directory {request.TargetDirectory} is not empty.");
                break;
            case LibraryMethods.CreateGlobalLibrary:
                RequireDirectory(request.TargetDirectory, "targetDirectory");
                RequireLibraryFileName(request.Name);
                plan.Target = $"new user global library '{request.Name}' in {request.TargetDirectory}";
                plan.Consequences.Add($"Creates library files in {request.TargetDirectory}.");
                break;
            case LibraryMethods.SaveGlobalLibrary:
            {
                var library = LibraryResolver.Resolve(portal, project, request.Library);
                LibraryResolver.RequireUserGlobal(library, request.Operation);
                plan.Library = library.Ref;
                plan.Target = $"save {library.Ref.Path}";
                plan.Consequences.Add($"Overwrites the library file {library.Ref.Path}.");
                break;
            }
            case LibraryMethods.CloseGlobalLibrary:
            {
                var library = LibraryResolver.Resolve(portal, project, request.Library);
                LibraryResolver.RequireUserGlobal(library, request.Operation, LibraryResolver.CloseSourcedForUserLibrariesOnly);
                plan.Library = library.Ref;
                plan.Target = $"close {library.Ref.Path}";
                plan.Consequences.Add("Close() takes no save-or-discard argument and no documented member reports unsaved changes: save first with "
                    + "save_global_library; a TIA Portal confirmation raised by Close() is answered by portalConfirmations and recorded.");
                break;
            }
            default:
                if (!PlanLifecycleExtended(portal, project, request, plan)) PlanContent(portal, project, request, plan);
                break;
        }

        return plan;
    }

    private static void PlanContent(TiaPortal portal, ProjectBase project, LibraryRequestInfo request, LibraryPlanInfo plan)
    {
        var library = LibraryResolver.Resolve(portal, project, request.Library);
        plan.Library = library.Ref;
        if (PlanContentExtended(project, library, request, plan, portal)) return;
        switch (request.Operation)
        {
            case LibraryMethods.CreateMasterCopy:
                RequireWritable(library, request.Operation);
                _ = LibraryResolver.ResolveMasterCopyFolder(library.Library, request.MasterCopyFolderPath);
                plan.Target = $"master copy of {request.Source!.Kind} '{request.Source.Path}' in the master copy folder {LibraryPolicy.FormatPath(LibraryResolver.Segments(request.MasterCopyFolderPath))}";
                _ = Source(SourceProject(portal, project, request.Source), request.Source);
                break;
            case LibraryMethods.CopyMasterCopy:
            {
                var masterCopy = LibraryResolver.ResolveMasterCopy(library.Library, request.MasterCopyName, request.MasterCopyFolderPath);
                var target = LibraryResolver.Resolve(portal, project, request.TargetLibrary);
                RequireWritable(target, request.Operation);
                _ = LibraryResolver.ResolveMasterCopyFolder(target.Library, request.TargetMasterCopyFolderPath);
                plan.Target = $"copy master copy '{masterCopy.Name}' to the master copy folder {LibraryPolicy.FormatPath(LibraryResolver.Segments(request.TargetMasterCopyFolderPath))} of {target.Ref.Library}";
                break;
            }
            case LibraryMethods.DeleteMasterCopy:
            {
                var masterCopy = LibraryResolver.ResolveMasterCopy(library.Library, request.MasterCopyName, request.MasterCopyFolderPath);
                plan.Target = $"master copy '{masterCopy.Name}'";
                plan.Consequences.Add("Deletes the master copy" + ContentText(masterCopy) + ".");
                break;
            }
            case LibraryMethods.RenameMasterCopy:
            {
                var masterCopy = LibraryResolver.ResolveMasterCopy(library.Library, request.MasterCopyName, request.MasterCopyFolderPath);
                plan.Target = $"master copy '{masterCopy.Name}' -> '{request.NewName}'";
                plan.Consequences.Add("The master copy's content keeps its own names.");
                break;
            }
            case LibraryMethods.InstantiateMasterCopy:
            {
                var masterCopy = LibraryResolver.ResolveMasterCopy(library.Library, request.MasterCopyName, request.MasterCopyFolderPath);
                var target = MasterCopyTarget(project, request.Target!);
                plan.Target = $"instance of master copy '{masterCopy.Name}'{ContentText(masterCopy)} in {target.Description}";
                if (request.MasterCopyMode == "Replace") plan.Consequences.Add($"MasterCopyMode Replace replaces existing objects of the same names in {target.Description}.");
                if (request.NewName is not null) plan.Consequences.Add($"The created object is renamed to '{request.NewName}' after creation (SetAttribute(\"Name\")).");
                break;
            }
            case LibraryMethods.InstantiateTypeVersion:
            {
                var (_, path, version) = LibraryResolver.ResolveVersion(library.Library, request);
                var target = TypeVersionTarget(project, request.Target!, version);
                plan.Target = $"instance of type {path ?? request.TypeGuid ?? request.VersionGuid} version {LibraryResolver.VersionText(version, new List<string>())} in {target.Description}";
                if (!LibraryPolicy.IsProject(request.Library))
                    plan.Consequences.Add("A global-library type version is synchronized into the project library before the instance is created.");
                break;
            }
            case LibraryMethods.UpdateProject:
            {
                var (_, description) = Selection(library, request);
                var plc = PlcSoftwareLocator.FindUnique(project, request.PlcName);
                _ = Scope(plc);
                plan.Target = $"update PLC '{plc.Software.Name}' from {description}";
                plan.Consequences.Add(Modes(request) is { } modes ? $"UpdateProject with {modes}."
                    : "The two-argument UpdateProject applies TIA Portal's default force-update, unused-version and structure-conflict behavior.");
                plan.Consequences.Add("Run library_read update_check first to see what is out of date.");
                break;
            }
            case LibraryMethods.RenameType:
            {
                var (type, path) = LibraryResolver.ResolveType(library.Library, request.TypePath, request.TypeGuid);
                plan.Target = $"type {path ?? type.Guid.ToString()} -> '{request.NewName}'";
                plan.Consequences.Add("Versions and existing instances are not renamed.");
                break;
            }
            case LibraryMethods.RenameTypeFolder:
            {
                var segments = LibraryResolver.Segments(request.FolderPath);
                if (segments.Count == 0) throw LibraryResolver.Validation("rename_type_folder names a user folder; the root type folder cannot be renamed.");
                _ = LibraryResolver.ResolveFolder(library.Library, segments);
                plan.Target = $"type folder {LibraryPolicy.FormatPath(segments)} -> '{request.NewName}'";
                break;
            }
            default:
            {
                var (_, path, version) = LibraryResolver.ResolveVersion(library.Library, request);
                var unreadable = new List<string>();
                var summary = LibraryResolver.Summary(version, unreadable);
                plan.Target = $"type {path ?? request.TypeGuid ?? "?"} version {summary.VersionNumber ?? summary.Guid} ({summary.State})";
                switch (request.Operation)
                {
                    case LibraryMethods.EditTypeVersion:
                        plan.Consequences.Add("Edit() returns an in-work version; edit its test instance, compile, then release_type_version.");
                        break;
                    case LibraryMethods.ReleaseTypeVersion:
                        plan.Consequences.Add($"Releases the version as {request.VersionNumber} with dependencies mode {request.DependenciesMode ?? LibraryPolicy.DefaultDependenciesMode}.");
                        break;
                    case LibraryMethods.DiscardTypeVersion:
                        plan.Consequences.Add("Discard() discards the in-work version's changes.");
                        break;
                    case LibraryMethods.SetDefaultTypeVersion:
                        plan.Consequences.Add("SetAsDefault() makes this the type's default version.");
                        break;
                    case LibraryMethods.DeleteTypeVersion:
                        var dependents = LibraryReadService.References(version, "Dependents", unreadable);
                        plan.Consequences.Add(dependents.Count == 0 ? "No dependent version was read."
                            : "Dependent versions: " + string.Join(", ", dependents.Select(d => $"{d.TypeName} {d.VersionNumber}")) + ".");
                        break;
                    default:
                        throw LibraryResolver.Validation($"Unsupported library write operation '{request.Operation}'.");
                }

                break;
            }
        }
    }

    // ================================================================ mutation

    /// <param name="confirmations">
    /// The TIA Portal confirmations answered while this item runs. The dispatch opens the answering scope with the
    /// item's <c>portalConfirmations</c> policy and attaches it; every exit with evidence closes the scope first and
    /// then copies the record into the result (plc_download's rule), a failure carries it as
    /// <see cref="WorkerOperationException.PlcDownloadOutcome"/> (the generic action-evidence channel).
    /// </param>
    public static LibraryWriteResultInfo Write(TiaPortal portal, ProjectBase project, string method, LibraryRequestInfo request,
        PlcDownloadPortalConfirmationLog? confirmations = null)
    {
        var operation = LibraryMethods.OperationOf(method) ?? throw LibraryResolver.Validation($"Unsupported library write method '{method}'.");
        if (!PlcDownloadPolicy.PortalConfirmationPolicies.IsKnown(request.PortalConfirmations))
            throw LibraryResolver.Validation($"portalConfirmations must be one of: {string.Join(", ", PlcDownloadPolicy.PortalConfirmationPolicies.All)}.");
        var result = new LibraryWriteResultInfo { Operation = operation, PortalConfirmationPolicy = request.PortalConfirmations! };
        try
        {
            Mutate(portal, project, operation, request, result);
        }
        catch (NonRecoverableException ex)
        {
            throw Failure(WorkerFailureCategories.PortalLost,
                $"The TIA Portal connection was lost during {operation}: {ex.Message}. Restart TIA Portal and explicitly bind_project again. "
                + "An attempted library change may have completed; inspect it before retrying.", null, null, result, confirmations);
        }
        catch (EngineeringException ex)
        {
            var category = WorkerExecution.Classify(ex, mutates: true);
            throw Failure(category, WorkerExecution.FailureMessage(ex, category), null, WorkerExecution.Describe(ex), result, confirmations);
        }
        catch (WorkerOperationException ex) when (ex.PlcDownloadOutcome is null)
        {
            throw Failure(ex.FailureCategory, ex.Message, ex.Warnings, ex.EngineeringError, result, confirmations);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            throw Failure(WorkerFailureCategories.WorkerOperationFailed, ex.Message, null, null, result, confirmations);
        }

        Closed(result, confirmations);
        return result;
    }

    /// <summary>Ends the item's confirmation scope and copies what it answered into the result.</summary>
    private static void Closed(LibraryWriteResultInfo result, PlcDownloadPortalConfirmationLog? confirmations)
        => (confirmations ?? new PlcDownloadPortalConfirmationLog()).CloseInto((entries, observed, complete) =>
        {
            result.PortalConfirmations = entries;
            result.PortalConfirmationsObserved = observed;
            result.PortalConfirmationsComplete = complete;
        });

    private static WorkerOperationException Failure(string category, string message, IReadOnlyList<string>? warnings,
        EngineeringErrorInfo? engineeringError, LibraryWriteResultInfo evidence, PlcDownloadPortalConfirmationLog? confirmations)
    {
        Closed(evidence, confirmations);
        return new(category, message, warnings, engineeringError: engineeringError, plcDownloadOutcome: WorkerJson.SerializePayload(evidence));
    }

    private static LibraryWriteResultInfo Mutate(TiaPortal portal, ProjectBase project, string operation, LibraryRequestInfo request,
        LibraryWriteResultInfo result)
    {
        switch (operation)
        {
            case LibraryMethods.OpenGlobalLibrary:
                return Open(portal, request, result);
            case LibraryMethods.RetrieveGlobalLibrary:
            {
                RequireFile(request.Path, "path");
                result.Target = $"archive {request.Path}";
                var before = LibraryResolver.OpenGlobals(portal, new List<string>());
                var call = request.Upgrade ? "RetrieveWithUpgrade" : "Retrieve";
                var opened = LibraryReflection.Invoke(portal.GlobalLibraries, call, new[] { typeof(FileInfo), typeof(DirectoryInfo), typeof(Siemens.Engineering.OpenMode) },
                    new[] { new FileInfo(request.Path!), new DirectoryInfo(request.TargetDirectory!), OpenModeValue(request) },
                    $"GlobalLibraryComposition.{call}(FileInfo, DirectoryInfo, OpenMode)");
                return Opened(portal, opened, result, call, before);
            }
            case LibraryMethods.CreateGlobalLibrary:
            {
                RequireDirectory(request.TargetDirectory, "targetDirectory");
                RequireLibraryFileName(request.Name);
                result.Target = $"new library '{request.Name}' in {request.TargetDirectory}";
                var before = LibraryResolver.OpenGlobals(portal, new List<string>());
                var created = LibraryReflection.InvokeGeneric(portal.GlobalLibraries, "Create", new[] { typeof(UserGlobalLibrary) },
                    new[] { typeof(DirectoryInfo), typeof(string) }, new object[] { new DirectoryInfo(request.TargetDirectory!), request.Name! },
                    "GlobalLibraryComposition.Create<UserGlobalLibrary>(DirectoryInfo, string)");
                return Opened(portal, created, result, "Create<UserGlobalLibrary>", before);
            }
            case LibraryMethods.SaveGlobalLibrary:
            {
                var library = LibraryResolver.Resolve(portal, project, request.Library);
                var user = LibraryResolver.RequireUserGlobal(library, operation);
                result.Library = library.Ref;
                result.Target = $"save {library.Ref.Path}";
                result.ReturnedType = LibraryReflection.RuntimeType(LibraryReflection.Invoke(user, "Save", Type.EmptyTypes, Array.Empty<object>(), "UserGlobalLibrary.Save()"));
                return result;
            }
            case LibraryMethods.CloseGlobalLibrary:
            {
                var library = LibraryResolver.Resolve(portal, project, request.Library);
                var user = LibraryResolver.RequireUserGlobal(library, operation, LibraryResolver.CloseSourcedForUserLibrariesOnly);
                result.Library = library.Ref;
                result.Target = $"close {library.Ref.Path}";
                result.ReturnedType = LibraryReflection.RuntimeType(LibraryReflection.Invoke(user, "Close", Type.EmptyTypes, Array.Empty<object>(), "UserGlobalLibrary.Close()"));
                if (LibraryResolver.OpenGlobals(portal, new List<string>()).Any(g => LibraryPolicy.SamePath(g.Ref.Path, library.Ref.Path)))
                    throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                        $"Close() returned, but {library.Ref.Path} is still listed among the open global libraries.");
                return result;
            }
        }

        if (MutateLifecycleExtended(portal, project, operation, request, result) is { } lifecycle) return lifecycle;
        if (operation == LibraryMethods.SetPortalSetting)
        {
            result.Setting = PortalSettings.Write(portal, request);
            result.Target = $"TIA Portal setting {result.Setting.FolderPath}/{result.Setting.Name}";
            result.Note = $"Read back: {result.Setting.After ?? "null"} (before: {result.Setting.Before ?? "null"}).";
            return result;
        }

        var resolved = LibraryResolver.Resolve(portal, project, request.Library);
        result.Library = resolved.Ref;
        if (MutateContentExtended(portal, project, resolved, operation, request, result) is { } extended) return extended;
        switch (operation)
        {
            case LibraryMethods.CreateMasterCopy:
            {
                RequireWritable(resolved, operation);
                var source = Source(SourceProject(portal, project, request.Source!), request.Source!);
                result.Target = $"master copy of {request.Source!.Kind} '{request.Source.Path}'";
                var created = LibraryResolver.ResolveMasterCopyFolder(resolved.Library, request.MasterCopyFolderPath).MasterCopies.Create(source);
                result.Created.Add(Created(created));
                return result;
            }
            case LibraryMethods.CopyMasterCopy:
            {
                var masterCopy = LibraryResolver.ResolveMasterCopy(resolved.Library, request.MasterCopyName, request.MasterCopyFolderPath);
                var target = LibraryResolver.Resolve(portal, project, request.TargetLibrary);
                RequireWritable(target, operation);
                result.Target = $"copy of master copy '{masterCopy.Name}' in {target.Ref.Library}";
                result.Created.Add(Created(LibraryResolver.ResolveMasterCopyFolder(target.Library, request.TargetMasterCopyFolderPath).MasterCopies.CreateFrom(masterCopy)));
                return result;
            }
            case LibraryMethods.DeleteMasterCopy:
            {
                var masterCopy = LibraryResolver.ResolveMasterCopy(resolved.Library, request.MasterCopyName, request.MasterCopyFolderPath);
                var name = masterCopy.Name;
                result.Target = $"master copy '{name}'";
                result.ReturnedType = LibraryReflection.RuntimeType(LibraryReflection.Invoke(masterCopy, "Delete", Type.EmptyTypes, Array.Empty<object>(), "MasterCopy.Delete()"));
                if (LibraryResolver.ResolveMasterCopyFolder(resolved.Library, request.MasterCopyFolderPath).MasterCopies.Find(name) is not null)
                    throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"Delete() returned, but master copy '{name}' is still found.");
                return result;
            }
            case LibraryMethods.RenameMasterCopy:
            {
                var masterCopy = LibraryResolver.ResolveMasterCopy(resolved.Library, request.MasterCopyName, request.MasterCopyFolderPath);
                result.Target = $"master copy '{masterCopy.Name}'";
                result.NameAfter = LibraryReflection.Rename(masterCopy, request.NewName!, $"Master copy '{masterCopy.Name}'");
                return result;
            }
            case LibraryMethods.InstantiateMasterCopy:
                return InstantiateMasterCopy(project, resolved, request, result);
            case LibraryMethods.InstantiateTypeVersion:
                return InstantiateTypeVersion(project, resolved, request, result);
            case LibraryMethods.UpdateProject:
            {
                var (selection, description) = Selection(resolved, request);
                var plc = PlcSoftwareLocator.FindUnique(project, request.PlcName);
                result.Target = $"PLC '{plc.Software.Name}' from {description}";
                var returned = HasModes(request)
                    ? UpdateProjectWithModes(resolved, selection, Scope(plc), request)
                    : LibraryReflection.InvokeWithCollections(resolved.Raw, "UpdateProject",
                        new[] { typeof(ILibraryTypeOrFolderSelection), typeof(IUpdateProjectScope) },
                        new IReadOnlyList<object>[] { new object[] { selection }, new object[] { Scope(plc) } },
                        "UpdateProject(<ILibraryTypeOrFolderSelection collection>, <IUpdateProjectScope collection>)");
                result.ReturnedType = LibraryReflection.RuntimeType(returned);
                result.Note = "Inspect the updated instances with library_read find_type_instances and run compile_check.";
                return result;
            }
            case LibraryMethods.RenameType:
            {
                var (type, path) = LibraryResolver.ResolveType(resolved.Library, request.TypePath, request.TypeGuid);
                result.Target = $"type {path ?? type.Guid.ToString()}";
                result.NameAfter = LibraryReflection.Rename(type, request.NewName!, $"Library type '{LibraryResolver.SafeName(type)}'");
                if (!string.Equals(LibraryResolver.SafeName(type), request.NewName, StringComparison.Ordinal))
                    throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"LibraryType.Name reads '{LibraryResolver.SafeName(type)}' after the rename.");
                return result;
            }
            case LibraryMethods.RenameTypeFolder:
            {
                var segments = LibraryResolver.Segments(request.FolderPath);
                if (segments.Count == 0) throw LibraryResolver.Validation("rename_type_folder names a user folder; the root type folder cannot be renamed.");
                var folder = LibraryResolver.ResolveFolder(resolved.Library, segments);
                result.Target = $"type folder {LibraryPolicy.FormatPath(segments)}";
                result.NameAfter = LibraryReflection.Rename(folder, request.NewName!, $"Type folder '{LibraryPolicy.FormatPath(segments)}'");
                return result;
            }
        }

        return VersionOperation(project, resolved, request, result);
    }

    private static LibraryWriteResultInfo Open(TiaPortal portal, LibraryRequestInfo request, LibraryWriteResultInfo result)
    {
        RequireFile(request.Path, "path");
        result.Target = $"global library file {request.Path}";
        var before = LibraryResolver.OpenGlobals(portal, new List<string>());
        var existing = LibraryResolver.FindOpenFile(before, request.Path!, "path");
        if (existing is not null)
        {
            result.AlreadyOpen = true;
            result.Library = existing.Ref;
            result.Note = "The file was already open; Open was not called and the library's open mode was not changed.";
            return result;
        }

        if (InfoToOpen(portal, request) is { } byInfo)
        {
            // K80: TIA Portal refuses a path for system and corporate libraries ("cannot be opened with a path").
            var viaInfo = LibraryReflection.Invoke(portal.GlobalLibraries, "Open", new[] { byInfo.Info.GetType() }, new[] { byInfo.Info },
                GlobalLibraryInfoOpen.Documented);
            var openedByInfo = Opened(portal, viaInfo, result, GlobalLibraryInfoOpen.Call, before);
            openedByInfo.Note ??= $"{byInfo.LibraryType} global library opened with {GlobalLibraryInfoOpen.Documented} (no OpenMode; TIA Portal opens it read-only).";
            return openedByInfo;
        }

        var opened = request.Upgrade
            ? LibraryReflection.Invoke(portal.GlobalLibraries, "OpenWithUpgrade", new[] { typeof(FileInfo) }, new object[] { new FileInfo(request.Path!) },
                "GlobalLibraryComposition.OpenWithUpgrade(FileInfo)")
            : LibraryReflection.Invoke(portal.GlobalLibraries, "Open", new[] { typeof(FileInfo), typeof(Siemens.Engineering.OpenMode) },
                new[] { new FileInfo(request.Path!), OpenModeValue(request) }, "GlobalLibraryComposition.Open(FileInfo, OpenMode)");
        return Opened(portal, opened, result, request.Upgrade ? "OpenWithUpgrade" : "Open", before);
    }

    /// <summary>
    /// The library an open/retrieve/create call returned, found again among the open global libraries. When it was already
    /// open before the call (<paramref name="before"/>: the same live object by <c>object.Equals</c>, or the same
    /// <c>Path.FullName</c>), the result says <c>alreadyOpen</c>: the call opened nothing new, so the host keeps the unsaved
    /// changes it recorded for that library.
    /// </summary>
    private static LibraryWriteResultInfo Opened(TiaPortal portal, object? returned, LibraryWriteResultInfo result, string call,
        IReadOnlyList<ResolvedLibrary> before)
    {
        result.ReturnedType = LibraryReflection.RuntimeType(returned);
        if (returned is not GlobalLibrary library)
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"{call} returned {(returned is null ? "nothing" : "a " + returned.GetType().FullName)}, not a global library; inspect library_read list_libraries.");
        var match = LibraryResolver.OpenGlobals(portal, new List<string>()).FirstOrDefault(g => g.Raw.Equals(library))
            ?? throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"{call} returned a library that is not listed among the open global libraries (or has no readable path).");
        result.Library = match.Ref;
        if (before.Any(g => g.Raw.Equals(library) || LibraryPolicy.SamePath(g.Ref.Path, match.Ref.Path)))
        {
            result.AlreadyOpen = true;
            result.Note = $"{call} returned a library that was already open before the call ({match.Ref.Path}); it is reported as already open "
                + "(no library was newly opened), so unsaved changes this server recorded for it are kept.";
        }

        return result;
    }

    private static LibraryWriteResultInfo InstantiateMasterCopy(ProjectBase project, ResolvedLibrary library, LibraryRequestInfo request, LibraryWriteResultInfo result)
    {
        var masterCopy = LibraryResolver.ResolveMasterCopy(library.Library, request.MasterCopyName, request.MasterCopyFolderPath);
        var target = MasterCopyTarget(project, request.Target!);
        result.Target = $"master copy '{masterCopy.Name}' in {target.Description}";
        Type returnType;
        var returned = LibraryPolicy.TargetKinds.TakesMasterCopyMode(request.Target!.Kind)
            ? LibraryReflection.Invoke(target.Composition, "CreateFrom", new[] { typeof(MasterCopy), typeof(MasterCopyMode) },
                new[] { masterCopy, LibraryReflection.EnumValue(typeof(MasterCopyMode), request.MasterCopyMode ?? LibraryPolicy.DefaultMasterCopyMode, "MasterCopyMode") },
                target.Documented, out returnType)
            : LibraryReflection.Invoke(target.Composition, "CreateFrom", new[] { typeof(MasterCopy) }, new object[] { masterCopy }, target.Documented, out returnType);
        return Placed(returned, returnType, target.Documented, request.NewName, result);
    }

    private static LibraryWriteResultInfo InstantiateTypeVersion(ProjectBase project, ResolvedLibrary library, LibraryRequestInfo request, LibraryWriteResultInfo result)
    {
        var (_, path, version) = LibraryResolver.ResolveVersion(library.Library, request);
        var target = TypeVersionTarget(project, request.Target!, version);
        result.Target = $"type {path ?? request.TypeGuid ?? request.VersionGuid} version {LibraryResolver.VersionText(version, new List<string>())} in {target.Description}";
        var versionType = version is CodeBlockLibraryTypeVersion ? typeof(CodeBlockLibraryTypeVersion) : typeof(PlcTypeLibraryTypeVersion);
        var returned = LibraryReflection.Invoke(target.Composition, "CreateFrom", new[] { versionType, typeof(UpdatePathsMode) },
            new[] { version, LibraryReflection.EnumValue(typeof(UpdatePathsMode), request.UpdatePathsMode ?? LibraryPolicy.DefaultUpdatePathsMode, "UpdatePathsMode") },
            target.Documented, out var returnType);
        return Placed(returned, returnType, target.Documented, null, result);
    }

    /// <summary>
    /// Projects what CreateFrom returned (its type is undocumented) and applies G's rename-after-placement. Evidence: a member
    /// that returns a value must return the created object(s): null is <c>postcondition_failed</c> (nothing was reported
    /// created). A member bound with no return value (<c>void</c>) or an empty collection cannot show what it created: the
    /// item is <c>unverified</c>, never trusted.
    /// </summary>
    private static LibraryWriteResultInfo Placed(object? returned, Type returnType, string documented, string? newName, LibraryWriteResultInfo result)
    {
        result.ReturnedType = LibraryReflection.RuntimeType(returned);
        if (returned is null && returnType != typeof(void))
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"{documented} returned null: no created object was reported. Inspect the target (plc_read, network_read) before retrying.");
        var objects = returned is IEnumerable enumerable and not string
            ? LibraryReflection.Enumerate(enumerable, "created objects").ToArray()
            : returned is null ? Array.Empty<object>() : new[] { returned };
        if (returned is null)
            Unverified(result, $"{documented} is bound without a return value on this installation, so the created object is not read back.");
        else if (objects.Length == 0)
            Unverified(result, $"{documented} returned an empty collection, so what it created is not read back.");
        result.Created.AddRange(objects.Take(LibraryPolicy.MaxInstances).Select(Created));
        if (newName is null) return result;
        if (objects.Length != 1)
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"CreateFrom returned {objects.Length} objects; newName renames exactly one. The created objects keep their library names.");
        result.NameAfter = LibraryReflection.Rename(objects[0], newName, $"Created object '{result.Created[0].Name}'");
        result.Created[0].Name = result.NameAfter;
        return result;
    }

    private static LibraryWriteResultInfo VersionOperation(ProjectBase project, ResolvedLibrary library, LibraryRequestInfo request, LibraryWriteResultInfo result)
    {
        var (type, path, version) = LibraryResolver.ResolveVersion(library.Library, request);
        var unreadable = new List<string>();
        var before = LibraryResolver.Summary(version, unreadable);
        result.Target = $"type {path ?? (type is null ? null : LibraryResolver.SafeName(type)) ?? "?"} version {before.VersionNumber ?? before.Guid}";
        switch (result.Operation)
        {
            case LibraryMethods.EditTypeVersion:
            {
                // V21: Edit(LibraryTypeInstanceInfo) "should be used by type that require instance to edit the type".
                var inWork = request.BlockPath is not null || request.PlcTypePath is not null
                    ? LibraryReflection.Invoke(version, "Edit", new[] { typeof(LibraryTypeInstanceInfo) },
                        new object[] { LibraryReadService.InstanceInfoOf(project, request.BlockPath, request.PlcTypePath) },
                        "LibraryTypeVersion.Edit(LibraryTypeInstanceInfo)") as LibraryTypeVersion
                    : version.Edit();
                if (inWork is null) throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, "Edit() returned no version.");
                result.Created.Add(new LibraryCreatedObjectInfo { Name = LibraryResolver.VersionText(inWork, unreadable), RuntimeType = LibraryReflection.RuntimeType(inWork) });
                // Evidence: Edit() returns the in-work version (L).
                ReadBack(inWork, "Edit()", result);
                CheckState(inWork, "InWork", "Edit()", result);
                return result;
            }
            case LibraryMethods.ReleaseTypeVersion:
            {
                var guid = version.Guid;
                result.ReturnedType = LibraryReflection.RuntimeType(LibraryReflection.Invoke(version, "Release",
                    new[] { typeof(CreateOrReleaseDependenciesMode), typeof(Version), typeof(string), typeof(string) },
                    new[] { LibraryReflection.EnumValue(typeof(CreateOrReleaseDependenciesMode), request.DependenciesMode ?? LibraryPolicy.DefaultDependenciesMode, "CreateOrReleaseDependenciesMode"),
                        Version.Parse(request.VersionNumber!), request.Author ?? string.Empty, request.Comment ?? string.Empty },
                    "LibraryTypeVersion.Release(CreateOrReleaseDependenciesMode, Version, string, string)"));
                // Evidence: the released version is Committed and carries the requested version number. It is read on the
                // same object, or found again by its GUID (ILibrary.FindVersion) when that object cannot be read any more.
                var released = Again(library.Library, version, guid);
                if (released is null)
                {
                    Unverified(result, $"Release(...) returned, but version {guid} could not be read again (neither the object nor ILibrary.FindVersion), "
                        + "so its state and number are not verified.");
                    return result;
                }

                ReadBack(released, "Release(...)", result);
                CheckState(released, "Committed", "Release(...)", result);
                var number = LibraryResolver.VersionText(released, new List<string>());
                if (number is null)
                    Unverified(result, "Release(...) returned, but LibraryTypeVersion.VersionNumber (a runtime lookup) could not be read, so the released number is not verified.");
                else if (!LibraryPolicy.SameVersionNumber(number, request.VersionNumber!))
                    throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                        $"Release(...) returned, but the version number reads '{number}', not the requested '{request.VersionNumber}'.");
                return result;
            }
            case LibraryMethods.DiscardTypeVersion:
            {
                var guid = version.Guid;
                result.ReturnedType = LibraryReflection.RuntimeType(LibraryReflection.Invoke(version, "Discard", Type.EmptyTypes, Array.Empty<object>(), "LibraryTypeVersion.Discard()"));
                // Evidence: the in-work version is gone (ILibrary.FindVersion finds nothing) or its state changed.
                LibraryTypeVersion? found;
                string? stateAfter;
                try
                {
                    found = library.Library.FindVersion(guid);
                    stateAfter = found?.State.ToString();
                }
                catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
                {
                    Unverified(result, $"Discard() returned, but version {guid} could not be looked up again (ILibrary.FindVersion), so the discard is not verified.");
                    return result;
                }

                if (found is null)
                {
                    result.Note = $"Discard() returned and version {guid} is no longer found (ILibrary.FindVersion).";
                    return result;
                }

                ReadBack(found, "Discard()", result);
                if (string.Equals(stateAfter, before.State, StringComparison.Ordinal))
                    throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                        $"Discard() returned, but version {guid} is still found in state {stateAfter} (ILibrary.FindVersion).");
                result.Note = $"Discard() returned; version {guid} is still found, in state {stateAfter} instead of {before.State}.";
                return result;
            }
            case LibraryMethods.SetDefaultTypeVersion:
                result.ReturnedType = LibraryReflection.RuntimeType(LibraryReflection.Invoke(version, "SetAsDefault", Type.EmptyTypes, Array.Empty<object>(), "LibraryTypeVersion.SetAsDefault()"));
                ReadBack(version, "SetAsDefault()", result);
                if (result.VersionAfter?.IsDefault == false)
                    throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, "SetAsDefault() returned, but IsDefault reads false.");
                if (result.VersionAfter is { IsDefault: null })
                    Unverified(result, "SetAsDefault() returned, but LibraryTypeVersion.IsDefault (a runtime lookup) could not be read, so the default is not verified.");
                return result;
            case LibraryMethods.DeleteTypeVersion:
            {
                var guid = version.Guid;
                result.ReturnedType = LibraryReflection.RuntimeType(LibraryReflection.Invoke(version, "Delete", Type.EmptyTypes, Array.Empty<object>(), "LibraryTypeVersion.Delete()"));
                if (library.Library.FindVersion(guid) is not null)
                    throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"Delete() returned, but version {guid} is still found (ILibrary.FindVersion).");
                return result;
            }
            default:
                throw LibraryResolver.Validation($"Unsupported library write operation '{result.Operation}'.");
        }
    }

    /// <summary>Adds why the item's postcondition could not be read back; the host then reports its verification as notVerified.</summary>
    private static void Unverified(LibraryWriteResultInfo result, string reason)
        => result.Unverified = result.Unverified is null ? reason : result.Unverified + " " + reason;

    /// <summary><see cref="LibraryWriteResultInfo.VersionAfter"/> read on the version; unverified when it cannot be read.</summary>
    private static void ReadBack(LibraryTypeVersion version, string call, LibraryWriteResultInfo result)
    {
        try
        {
            result.VersionAfter = LibraryResolver.Summary(version, new List<string>());
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
        {
            Unverified(result, $"{call} returned, but the version could not be read afterwards; read the type again with library_read get_type.");
        }
    }

    /// <summary>
    /// The version's <c>State</c> (compiled; L: <c>LibraryTypeVersionState</c> is "<c>InWork</c> or <c>Committed</c>") compared with
    /// the member named <paramref name="expected"/> of the live enum, found by exact name (<see cref="LibraryRuntimeBinding.EnumValue"/>;
    /// the stub enum declares no members). A different state is <c>postcondition_failed</c>; an unreadable state or a live enum
    /// without that member makes the item unverified, never trusted.
    /// </summary>
    private static void CheckState(LibraryTypeVersion version, string expected, string call, LibraryWriteResultInfo result)
    {
        object state;
        try
        {
            state = version.State;
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
        {
            Unverified(result, $"{call} returned, but LibraryTypeVersion.State could not be read, so the state {expected} is not verified.");
            return;
        }

        var wanted = LibraryRuntimeBinding.EnumValue(state.GetType(), expected);
        if (wanted is null)
        {
            Unverified(result, $"{call} returned, but LibraryTypeVersionState has no member '{expected}' on this installation "
                + $"(members: {LibraryRuntimeBinding.MemberList(state.GetType())}), so the state is not verified.");
            return;
        }

        if (!wanted.Equals(state))
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"{call} returned, but the version's State reads {state}, not {expected}.");
    }

    /// <summary>The version object if it can still be read, else the version found again by its GUID (ILibrary.FindVersion); null when neither.</summary>
    private static LibraryTypeVersion? Again(ILibrary library, LibraryTypeVersion version, Guid guid)
    {
        try
        {
            _ = version.State;
            return version;
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
        {
        }

        try
        {
            return library.FindVersion(guid);
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
        {
            return null;
        }
    }

    // ================================================================ targets and sources

    private sealed record PlacementTarget(object Composition, string Description, string Documented);

    private static PlacementTarget MasterCopyTarget(ProjectBase project, LibraryTargetInfo target)
    {
        switch (target.Kind)
        {
            case LibraryPolicy.TargetKinds.Devices:
                return new PlacementTarget(project.Devices, "the project's devices", "DeviceComposition.CreateFrom(MasterCopy)");
            case LibraryPolicy.TargetKinds.DeviceItem:
            {
                var parent = ParentItem(project, target.Parent!);
                return new PlacementTarget(parent.DeviceItems, $"device item '{SafeItemName(parent)}' of '{target.Parent!.DeviceName}'", "DeviceItemComposition.CreateFrom(MasterCopy)");
            }
        }

        var plc = PlcSoftwareLocator.FindUnique(project, target.PlcName);
        var folder = LibraryPolicy.FormatPath(LibraryResolver.Segments(target.FolderPath));
        return target.Kind switch
        {
            LibraryPolicy.TargetKinds.BlockGroup => new PlacementTarget(LibraryResolver.BlockGroup(plc, target.FolderPath).Blocks,
                $"block folder {folder} of PLC '{plc.Software.Name}'", "PlcBlockComposition.CreateFrom(MasterCopy, MasterCopyMode)"),
            LibraryPolicy.TargetKinds.TypeGroup => new PlacementTarget(LibraryResolver.TypeGroup(plc, target.FolderPath).Types,
                $"PLC data type folder {folder} of PLC '{plc.Software.Name}'", "PlcTypeComposition.CreateFrom(MasterCopy, MasterCopyMode)"),
            LibraryPolicy.TargetKinds.TagTableGroup => new PlacementTarget(LibraryResolver.TagTableGroup(plc, target.FolderPath).TagTables,
                $"tag table folder {folder} of PLC '{plc.Software.Name}'", "PlcTagTableComposition.CreateFrom(MasterCopy, MasterCopyMode)"),
            LibraryPolicy.TargetKinds.TechnologyObjects => new PlacementTarget(plc.Software.TechnologicalObjectGroup.TechnologicalObjects,
                $"technology objects of PLC '{plc.Software.Name}'", "TechnologicalInstanceDBComposition.CreateFrom(MasterCopy, MasterCopyMode)"),
            LibraryPolicy.TargetKinds.SoftwareUnits => new PlacementTarget(Units(plc), $"software units of PLC '{plc.Software.Name}'",
                "PlcUnitComposition.CreateFrom(MasterCopy, MasterCopyMode)"),
            LibraryPolicy.TargetKinds.ExternalSources => new PlacementTarget(plc.Software.ExternalSourceGroup.ExternalSources,
                $"external sources of PLC '{plc.Software.Name}'", "PlcExternalSourceComposition.CreateFrom(MasterCopy, MasterCopyMode)"),
            LibraryPolicy.TargetKinds.TextLists => new PlacementTarget(PlcAlarms.PlcTextListResolver.Group(plc).PlcAlarmUserTextlists,
                $"user text lists of PLC '{plc.Software.Name}'", "PlcAlarmUserTextlistComposition.CreateFrom(MasterCopy, MasterCopyMode)"),
            _ => throw LibraryResolver.Validation($"Unknown target kind '{target.Kind}'."),
        };
    }

    /// <summary>G/A: <c>plcSoftware.GetService&lt;PlcUnitProvider&gt;().UnitGroup.Units</c>.</summary>
    private static object Units(PlcSoftwareLocator.DiscoveredPlcSoftware plc)
        => plc.Software.GetService<Siemens.Engineering.SW.Units.PlcUnitProvider>()?.UnitGroup.Units
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
                $"PLC '{plc.Software.Name}' offers no PlcUnitProvider (software units are unavailable on this PLC).");

    private static PlacementTarget TypeVersionTarget(ProjectBase project, LibraryTargetInfo target, LibraryTypeVersion version)
    {
        var plc = PlcSoftwareLocator.FindUnique(project, target.PlcName);
        var folder = LibraryPolicy.FormatPath(LibraryResolver.Segments(target.FolderPath));
        switch (target.Kind)
        {
            case LibraryPolicy.TargetKinds.BlockGroup when version is CodeBlockLibraryTypeVersion:
                return new PlacementTarget(LibraryResolver.BlockGroup(plc, target.FolderPath).Blocks,
                    $"block folder {folder} of PLC '{plc.Software.Name}'", "PlcBlockComposition.CreateFrom(CodeBlockLibraryTypeVersion, UpdatePathsMode)");
            case LibraryPolicy.TargetKinds.TypeGroup when version is PlcTypeLibraryTypeVersion:
                return new PlacementTarget(LibraryResolver.TypeGroup(plc, target.FolderPath).Types,
                    $"PLC data type folder {folder} of PLC '{plc.Software.Name}'", "PlcTypeComposition.CreateFrom(PlcTypeLibraryTypeVersion, UpdatePathsMode)");
            default:
                throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
                    $"A {(version is CodeBlockLibraryTypeVersion ? "code block" : version is PlcTypeLibraryTypeVersion ? "PLC data type" : "'" + version.GetType().FullName + "'")} "
                    + $"type version cannot be instantiated into a {target.Kind} (G: code block versions into a blockGroup, PLC data type versions into a typeGroup).");
        }
    }

    private static DeviceItem ParentItem(ProjectBase project, HardwareObjectSelectorInfo parent)
    {
        if (parent.ItemPath is not { Count: > 0 }) throw LibraryResolver.Validation("target.parent needs a non-empty itemPath naming the parent device item.");
        var resolution = NetworkObjectSelectorResolver.Resolve(project, new NetworkObjectSelectorInfo
        {
            Kind = NetworkObjectKinds.DeviceItem,
            DeviceName = parent.DeviceName,
            ItemPath = parent.ItemPath.Select(segment => new DeviceItemPathSegmentInfo
            {
                Index = segment.Index, Name = segment.Name, PositionNumber = segment.PositionNumber, TypeIdentifier = segment.TypeIdentifier,
            }).ToList(),
        });
        if (!resolution.Success)
            throw new WorkerOperationException(resolution.FailureCategory ?? WorkerFailureCategories.TargetNotFound, $"The parent device item could not be resolved: {resolution.Error}");
        return resolution.Resolved!.Value as DeviceItem ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, "target.parent does not name a device item.");
    }

    private static string? SafeItemName(DeviceItem item)
    {
        try
        {
            return item.Name;
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
        {
            return null;
        }
    }

    /// <summary>The project object for <c>MasterCopyComposition.Create(IMasterCopySource)</c>; it must be an <c>IMasterCopySource</c> (L).</summary>
    private static IMasterCopySource Source(ProjectBase project, LibrarySourceInfo source)
    {
        object found = source.Kind switch
        {
            LibraryPolicy.SourceKinds.Block => BlockTargetResolver.ResolveForExport(project, BlockAddress.Parse(source.Path)).Block
                ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Block '{source.Path}' was not found."),
            LibraryPolicy.SourceKinds.PlcType => PlcTypeTargetResolver.ResolveForExport(project, PlcTypeAddress.Parse(source.Path)).Type
                ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"PLC data type '{source.Path}' was not found."),
            LibraryPolicy.SourceKinds.TagTable => TagTable(project, source),
            LibraryPolicy.SourceKinds.Device => Device(project, source.Path),
            LibraryPolicy.SourceKinds.TextList => PlcAlarms.PlcTextListResolver.User(PlcSoftwareLocator.FindUnique(project, source.PlcName), source.Path),
            _ => throw LibraryResolver.Validation($"Unknown source kind '{source.Kind}'."),
        };
        return found as IMasterCopySource ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
            $"The {source.Kind} '{source.Path}' ({found.GetType().FullName}) is not an IMasterCopySource; it cannot become a master copy.");
    }

    private static object TagTable(ProjectBase project, LibrarySourceInfo source)
    {
        var parsed = LibraryPolicy.ParseTypePath(source.Path, out var error) ?? throw LibraryResolver.Validation($"source.path: {error}");
        var plc = PlcSoftwareLocator.FindUnique(project, source.PlcName);
        var group = LibraryResolver.TagTableGroup(plc, LibraryPolicy.FormatPath(parsed.Folder));
        return group.TagTables.Find(parsed.Name) ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
            $"Tag table '{source.Path}' was not found in PLC '{plc.Software.Name}'.");
    }

    private static Device Device(ProjectBase project, string name)
    {
        var matches = ProjectDeviceNameMatcher.FindMatches(project, name);
        return matches.Count switch
        {
            1 => matches[0].Device,
            0 => throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No device named '{name}' was found."),
            _ => throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous, $"Multiple devices are named '{name}'."),
        };
    }

    /// <summary>The update selection: a type or a type folder, which must be an <c>ILibraryTypeOrFolderSelection</c> (L catalogue).</summary>
    private static (ILibraryTypeOrFolderSelection Selection, string Description) Selection(ResolvedLibrary library, LibraryRequestInfo request)
    {
        object selected;
        string description;
        if (request.TypePath is not null || request.TypeGuid is not null)
        {
            var (type, path) = LibraryResolver.ResolveType(library.Library, request.TypePath, request.TypeGuid);
            selected = type;
            description = $"type {path ?? type.Guid.ToString()}";
        }
        else
        {
            selected = LibraryResolver.ResolveFolder(library.Library, request.FolderPath);
            description = $"type folder {LibraryPolicy.FormatPath(LibraryResolver.Segments(request.FolderPath))}";
        }

        return (selected as ILibraryTypeOrFolderSelection ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
            $"The {description} is not an ILibraryTypeOrFolderSelection on this TIA Portal installation."), description + " of " + library.Ref.Library);
    }

    /// <summary>G: <c>new List&lt;IUpdateProjectScope&gt; { plcSoftware }</c>.</summary>
    private static IUpdateProjectScope Scope(PlcSoftwareLocator.DiscoveredPlcSoftware plc)
        => plc.Software as IUpdateProjectScope ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
            "This PLC software is not an IUpdateProjectScope on this TIA Portal installation.");

    // ================================================================ small helpers

    private static void RequireWritable(ResolvedLibrary library, string operation)
    {
        if (library.Raw is SystemGlobalLibrary or CorporateGlobalLibrary)
            throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
                $"{operation} changes a library; '{library.Ref.Library}' is a {library.Ref.Kind} library, which is read-only (L).");
    }

    private static LibraryCreatedObjectInfo Created(object created) => new()
    {
        Name = created is MasterCopy masterCopy ? SafeMasterCopyName(masterCopy) : LibraryReflection.Text(created, "Name", new List<string>(), "created object name"),
        RuntimeType = LibraryReflection.RuntimeType(created),
    };

    private static string? SafeMasterCopyName(MasterCopy masterCopy)
    {
        try
        {
            return masterCopy.Name;
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
        {
            return null;
        }
    }

    private static string ContentText(MasterCopy masterCopy)
    {
        var types = LibraryReadService.ContentTypes(masterCopy, new List<string>());
        return types.Count == 0 ? string.Empty : " (content: " + string.Join(", ", types.Select(LibraryPolicy.ShortTypeName)) + ")";
    }

    private static string OpenMode(LibraryRequestInfo request) => request.OpenMode ?? LibraryPolicy.DefaultOpenMode;

    /// <summary>
    /// K80: the <c>GlobalLibraryInfo</c> of <c>GetGlobalLibraryInfos()</c> (runtime lookup, its collection type is undocumented) whose
    /// <c>Path</c> is the request's and whose <c>LibraryType</c> is System or Corporate; null otherwise (the path route stays and
    /// TIA Portal decides). Options the info route cannot honour are refused before anything is opened.
    /// </summary>
    private static (object Info, string LibraryType)? InfoToOpen(TiaPortal portal, LibraryRequestInfo request)
    {
        var unreadable = new List<string>();
        var infos = LibraryReflection.Invoke(portal.GlobalLibraries, "GetGlobalLibraryInfos", Type.EmptyTypes, Array.Empty<object>(),
            "GlobalLibraryComposition.GetGlobalLibraryInfos()");
        var found = GlobalLibraryInfoOpen.Find(LibraryReflection.Enumerate(infos, "global library infos").Cast<object>(), request.Path!,
            info => LibraryReflection.Display(LibraryReflection.Optional(info, "Path", unreadable, "GlobalLibraryInfo.Path")),
            info => LibraryReflection.Display(LibraryReflection.Optional(info, "LibraryType", unreadable, "GlobalLibraryInfo.LibraryType")),
            out var ambiguity);
        if (ambiguity is not null) throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous, ambiguity);
        if (found is not { } match) return null;
        if (GlobalLibraryInfoOpen.OptionRefusal(request, match.LibraryType) is { } refusal) throw LibraryResolver.Validation(refusal);
        return match;
    }

    private static object OpenModeValue(LibraryRequestInfo request)
        => LibraryReflection.EnumValue(typeof(Siemens.Engineering.OpenMode), OpenMode(request), "OpenMode");

    private static void RequireFile(string? path, string field)
    {
        if (string.IsNullOrWhiteSpace(path)) throw LibraryResolver.Validation($"{field} is required.");
        if (LibraryPolicy.FilePathError(path) is { } pathError) throw LibraryResolver.Validation($"{field} {pathError}");
        if (!File.Exists(path)) throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"{field} '{path}' does not exist.");
    }

    /// <summary>The new library's name becomes a directory and file name below targetDirectory (<see cref="LibraryPolicy.LibraryFileNameError"/>).</summary>
    private static void RequireLibraryFileName(string? name)
    {
        if (LibraryPolicy.LibraryFileNameError(name) is { } error) throw LibraryResolver.Validation($"name {error}");
    }

    private static void RequireDirectory(string? path, string field)
    {
        if (string.IsNullOrWhiteSpace(path)) throw LibraryResolver.Validation($"{field} is required.");
        if (LibraryPolicy.FilePathError(path) is { } pathError) throw LibraryResolver.Validation($"{field} {pathError}");
        if (!Directory.Exists(path)) throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"{field} '{path}' does not exist.");
    }
}
