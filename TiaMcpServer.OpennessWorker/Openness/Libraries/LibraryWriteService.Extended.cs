using System.Security.Cryptography;
using Siemens.Engineering;
using Siemens.Engineering.Library;
using Siemens.Engineering.Library.MasterCopies;
using Siemens.Engineering.Library.Types;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Libraries;

/// <summary>
/// The library_write operations added from the V21 API description (issue #17): archive and save-as of a user global library,
/// clean-up, harmonize, update of a library, update_project with modes, type deletion and DoNotUse, type and master copy
/// folders, types and versions from documents, and type version exports. Every member is bound at runtime by its documented
/// name and parameter list (<see cref="LibrarySignatureBinding"/>: the enum parameter types are matched by their documented full
/// names, none is declared on the stubs); a member that does not bind fails closed with <c>unsupported_capability</c> before
/// anything is called. Not live-tested.
/// </summary>
internal static partial class LibraryWriteService
{
    // ================================================================ planning

    /// <summary>Planning of the lifecycle items archive_global_library and save_global_library_as; false for any other item.</summary>
    private static bool PlanLifecycleExtended(TiaPortal portal, ProjectBase project, LibraryRequestInfo request, LibraryPlanInfo plan)
    {
        switch (request.Operation)
        {
            case LibraryMethods.ArchiveGlobalLibrary:
            {
                var library = LibraryResolver.Resolve(portal, project, request.Library);
                LibraryResolver.RequireUserGlobal(library, request.Operation);
                RequireDirectory(request.TargetDirectory, "targetDirectory");
                RequireLibraryFileName(request.Name);
                plan.Library = library.Ref;
                plan.Target = $"archive {library.Ref.Path} as '{request.Name}' in {request.TargetDirectory} (LibraryArchivationMode {Archivation(request)})";
                plan.Consequences.Add($"Writes the archive '{request.Name}' into {request.TargetDirectory}; the open library is not changed.");
                var existing = Matching(request.TargetDirectory!, request.Name!);
                if (existing.Count > 0) plan.Consequences.Add($"{request.TargetDirectory} already holds {string.Join(", ", existing.Select(Path.GetFileName))}; TIA Portal decides whether Archive replaces it.");
                return true;
            }
            case LibraryMethods.SaveGlobalLibraryAs:
            {
                var library = LibraryResolver.Resolve(portal, project, request.Library);
                LibraryResolver.RequireUserGlobal(library, request.Operation);
                RequireDirectory(request.TargetDirectory, "targetDirectory");
                plan.Library = library.Ref;
                plan.Target = $"save {library.Ref.Path} as a copy in {request.TargetDirectory}";
                plan.Consequences.Add($"Writes the library into {request.TargetDirectory} (UserGlobalLibrary.SaveAs(DirectoryInfo)); which file the open library refers to afterwards is read back.");
                if (Directory.EnumerateFileSystemEntries(request.TargetDirectory!).Any()) plan.Consequences.Add($"The target directory {request.TargetDirectory} is not empty.");
                return true;
            }
            case LibraryMethods.OpenSecondaryProject:
            {
                RequireFile(request.Path, "path");
                SecondaryProjects.RequireNotBound(project, request.Path!);
                plan.AlreadyOpen = SecondaryProjects.IsOpen(portal, request.Path!);
                plan.Target = $"secondary project {request.Path} ({(request.Upgrade ? "OpenWithUpgrade" : "Open")}, ProjectOpenMode.Secondary)";
                plan.Consequences.Add("Opens the project read-only as a secondary (reference) project next to the bound project; the binding does not change, "
                    + "content tools stay on the bound project. Copy objects from it with create_master_copy source.project, compare with plc_read compare_software compareTarget secondary_plc.");
                if (request.Upgrade) plan.Consequences.Add("OpenWithUpgrade may upgrade the project file on disk.");
                if (plan.AlreadyOpen) plan.Consequences.Add("TIA Portal already has a project with this path open; TIA Portal decides whether it opens it again.");
                return true;
            }
            case LibraryMethods.RetrieveSecondaryProject:
            {
                RequireFile(request.Path, "path");
                RequireDirectory(request.TargetDirectory, "targetDirectory");
                plan.Target = $"secondary project from archive {request.Path} into {request.TargetDirectory} ({(request.Upgrade ? "RetrieveWithUpgrade" : "Retrieve")}, ProjectOpenMode.Secondary)";
                plan.Consequences.Add($"Extracts the archive into {request.TargetDirectory} and opens the extracted project read-only as a secondary project; the binding does not change.");
                if (Directory.EnumerateFileSystemEntries(request.TargetDirectory!).Any()) plan.Consequences.Add($"The target directory {request.TargetDirectory} is not empty.");
                return true;
            }
            case LibraryMethods.CloseSecondaryProject:
            {
                var secondary = SecondaryProjects.Find(portal, request.SecondaryProject, "secondaryProject");
                plan.Target = $"close secondary project {SecondaryProjects.Describe(secondary).Path}";
                plan.Consequences.Add("Project.Close() closes the read-only reference project; the bound project stays open and bound.");
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>Planning of the added content items; false for any other item.</summary>
    private static bool PlanContentExtended(ProjectBase project, ResolvedLibrary library, LibraryRequestInfo request, LibraryPlanInfo plan, TiaPortal portal)
    {
        switch (request.Operation)
        {
            case LibraryMethods.CleanUpLibrary:
            {
                RequireWritable(library, request.Operation);
                if (!LibraryPolicy.IsProject(request.Library)) LibraryResolver.RequireUserGlobal(library, request.Operation);
                var (selection, description) = Selection(library, request);
                plan.Target = $"clean up {description}";
                plan.Consequences.Add(LibraryPolicy.IsProject(request.Library)
                    ? $"CleanUpLibrary with CleanUpMode {request.CleanUpMode ?? LibraryPolicy.DefaultCleanUpMode} deletes unused versions"
                        + (request.CleanUpMode == "DeleteUnusedTypes" ? " and types none of whose versions is used in the project." : " and keeps the default (or highest) version of unused types.")
                    : "UserGlobalLibrary.CleanUpLibrary deletes unused versions without deleting complete types.");
                plan.Consequences.Add("Currently " + ContentCount(selection) + ".");
                return true;
            }
            case LibraryMethods.HarmonizeProject:
            {
                var (_, description) = Selection(library, request);
                var plc = PlcSoftwareLocator.FindUnique(project, request.PlcName);
                _ = Scope(plc);
                plan.Target = $"harmonize PLC '{plc.Software.Name}' from {description} ({string.Join(", ", request.HarmonizeOptions ?? new List<string>())})";
                plan.Consequences.Add("HarmonizeProject renames or moves the PLC's instances to the library's "
                    + string.Join(" and ", (request.HarmonizeOptions ?? new List<string>()).Select(o => o == "HarmonizeNames" ? "names" : "paths")) + "; compile afterwards.");
                return true;
            }
            case LibraryMethods.UpdateLibrary:
            {
                var (_, description) = Selection(library, request);
                var target = LibraryResolver.Resolve(portal, project, request.TargetLibrary);
                RequireWritable(target, request.Operation);
                plan.Target = $"update {target.Ref.Library} from {description}";
                plan.Consequences.Add(Modes(request) is { } modes
                    ? $"UpdateLibrary with {modes}."
                    : "The two-argument UpdateLibrary applies TIA Portal's default force-update, unused-version and structure-conflict behavior.");
                return true;
            }
            case LibraryMethods.DeleteType:
            {
                RequireWritable(library, request.Operation);
                var (type, path) = LibraryResolver.ResolveType(library.Library, request.TypePath, request.TypeGuid);
                plan.Target = $"type {path ?? type.Guid.ToString()}";
                var versions = LibraryResolver.Versions(type);
                plan.Consequences.Add($"Deletes the type with its {versions.Count} version(s): "
                    + string.Join(", ", versions.Select(v => LibraryResolver.VersionText(v, new List<string>()) ?? v.Guid.ToString())) + ".");
                return true;
            }
            case LibraryMethods.SetTypeDoNotUse:
            {
                RequireWritable(library, request.Operation);
                var (type, path) = LibraryResolver.ResolveType(library.Library, request.TypePath, request.TypeGuid);
                var current = LibraryReflection.Optional(type, "DoNotUse", new List<string>(), "LibraryType.DoNotUse");
                plan.Target = $"type {path ?? type.Guid.ToString()} DoNotUse {current?.ToString() ?? "(unreadable)"} -> {request.DoNotUse}";
                return true;
            }
            case LibraryMethods.CreateTypeFolder:
            {
                RequireWritable(library, request.Operation);
                var segments = LibraryResolver.Segments(request.FolderPath);
                _ = LibraryResolver.ResolveFolder(library.Library, segments);
                plan.Target = $"new type folder {LibraryPolicy.FormatPath(segments.Concat(new[] { request.NewName! }))}";
                return true;
            }
            case LibraryMethods.DeleteTypeFolder:
            {
                RequireWritable(library, request.Operation);
                var segments = UserFolder(request.FolderPath, "type");
                var folder = LibraryResolver.ResolveFolder(library.Library, segments);
                plan.Target = $"type folder {LibraryPolicy.FormatPath(segments)}";
                plan.Consequences.Add($"Deletes the folder with everything in it: {LibraryResolver.Types(folder).Count} type(s), {LibraryResolver.ChildFolders(folder).Count} subfolder(s).");
                return true;
            }
            case LibraryMethods.CreateMasterCopyFolder:
            {
                RequireWritable(library, request.Operation);
                var segments = LibraryResolver.Segments(request.MasterCopyFolderPath);
                _ = LibraryResolver.ResolveMasterCopyFolder(library.Library, segments);
                plan.Target = $"new master copy folder {LibraryPolicy.FormatPath(segments.Concat(new[] { request.NewName! }))}";
                return true;
            }
            case LibraryMethods.DeleteMasterCopyFolder:
            {
                RequireWritable(library, request.Operation);
                var segments = UserFolder(request.MasterCopyFolderPath, "master copy");
                var folder = LibraryResolver.ResolveMasterCopyFolder(library.Library, segments);
                plan.Target = $"master copy folder {LibraryPolicy.FormatPath(segments)}";
                plan.Consequences.Add($"Deletes the folder with everything in it: {folder.MasterCopies.Count()} master copy(ies), "
                    + $"{LibraryResolver.MasterCopyChildFolders(folder).Count} subfolder(s).");
                return true;
            }
            case LibraryMethods.RenameMasterCopyFolder:
            {
                RequireWritable(library, request.Operation);
                var segments = UserFolder(request.MasterCopyFolderPath, "master copy");
                _ = LibraryResolver.ResolveMasterCopyFolder(library.Library, segments);
                plan.Target = $"master copy folder {LibraryPolicy.FormatPath(segments)} -> '{request.NewName}'";
                return true;
            }
            case LibraryMethods.CreateTypeFromDocuments:
            {
                RequireWritable(library, request.Operation);
                RequireDirectory(request.DocumentDirectory, "documentDirectory");
                var segments = LibraryResolver.Segments(request.FolderPath);
                _ = LibraryResolver.ResolveFolder(library.Library, segments);
                var environment = TargetEnvironment(project, request);
                plan.Target = $"new type from documents '{request.DocumentName}' in {request.DocumentDirectory} into type folder {LibraryPolicy.FormatPath(segments)}"
                    + (environment is null ? string.Empty : $" (target environment PLC '{environment.Value.Name}')");
                plan.Consequences.Add(DocumentsText(request.DocumentDirectory!, request.DocumentName!));
                return true;
            }
            case LibraryMethods.CreateVersionFromDocuments:
            {
                RequireWritable(library, request.Operation);
                RequireDirectory(request.DocumentDirectory, "documentDirectory");
                var (type, path) = LibraryResolver.ResolveType(library.Library, request.TypePath, request.TypeGuid);
                var environment = TargetEnvironment(project, request);
                plan.Target = $"new version of type {path ?? type.Guid.ToString()} from documents '{request.DocumentName}' in {request.DocumentDirectory} (CreateOptions {request.CreateOption})"
                    + (environment is null ? string.Empty : $" (target environment PLC '{environment.Value.Name}')");
                plan.Consequences.Add(DocumentsText(request.DocumentDirectory!, request.DocumentName!));
                if (request.CreateOption == "Override") plan.Consequences.Add("CreateOptions Override overwrites the type's current in-work or in-test version.");
                return true;
            }
            case LibraryMethods.ExportTypeVersion:
            {
                var (_, path, version) = LibraryResolver.ResolveVersion(library.Library, request);
                var target = ExportFile(request);
                plan.Target = $"export type {path ?? request.TypeGuid ?? request.VersionGuid} version {LibraryResolver.VersionText(version, new List<string>())} to {target}";
                plan.Consequences.Add(File.Exists(target) ? $"{target} exists and is replaced (overwrite:true)." : $"Writes the SimaticML file {target}.");
                return true;
            }
            case LibraryMethods.ExportTypeVersionAsDocuments:
            {
                var (_, path, version) = LibraryResolver.ResolveVersion(library.Library, request);
                RequireDirectory(request.DocumentDirectory, "documentDirectory");
                var existing = RequireExportNames(request);
                plan.Target = $"export type {path ?? request.TypeGuid ?? request.VersionGuid} version {LibraryResolver.VersionText(version, new List<string>())} "
                    + $"as {request.ExportFormat} documents '{request.DocumentName}' into {request.DocumentDirectory}";
                plan.Consequences.Add(existing.Count == 0 ? $"Writes documents named '{request.DocumentName}' into {request.DocumentDirectory}."
                    : $"{string.Join(", ", existing.Select(Path.GetFileName))} exist; with overwrite:true TIA Portal decides whether ExportAsDocuments replaces them.");
                return true;
            }
            default:
                return false;
        }
    }

    // ================================================================ mutation

    /// <summary>The lifecycle mutations archive_global_library and save_global_library_as; null for any other item.</summary>
    private static LibraryWriteResultInfo? MutateLifecycleExtended(TiaPortal portal, ProjectBase project, string operation, LibraryRequestInfo request, LibraryWriteResultInfo result)
    {
        switch (operation)
        {
            case LibraryMethods.ArchiveGlobalLibrary:
            {
                var library = LibraryResolver.Resolve(portal, project, request.Library);
                var user = LibraryResolver.RequireUserGlobal(library, operation);
                RequireDirectory(request.TargetDirectory, "targetDirectory");
                RequireLibraryFileName(request.Name);
                result.Library = library.Ref;
                result.Target = $"archive {library.Ref.Path} as '{request.Name}' in {request.TargetDirectory}";
                var started = DateTime.UtcNow.AddSeconds(-2);
                result.ReturnedType = LibraryReflection.RuntimeType(LibraryReflection.InvokeSignature(user, "Archive", new[]
                {
                    SignatureArgument.Of(new DirectoryInfo(request.TargetDirectory!)), SignatureArgument.Of(request.Name!),
                    SignatureArgument.Enum(LibraryPolicy.EnumTypes.ArchivationMode, Archivation(request)),
                }, "UserGlobalLibrary.Archive(DirectoryInfo, String, LibraryArchivationMode)"));
                // Evidence: a file named after targetName in targetDirectory, written by this call (its extension is TIA Portal's).
                var written = Matching(request.TargetDirectory!, request.Name!).Where(file => File.GetLastWriteTimeUtc(file) >= started).ToList();
                result.Transfer = new LibraryTransferInfo { Files = written.Select(Hash).ToList() };
                if (written.Count == 0)
                    Unverified(result, $"Archive(...) returned, but no file named '{request.Name}' written by this call was found in {request.TargetDirectory}.");
                return result;
            }
            case LibraryMethods.SaveGlobalLibraryAs:
            {
                var library = LibraryResolver.Resolve(portal, project, request.Library);
                var user = LibraryResolver.RequireUserGlobal(library, operation);
                RequireDirectory(request.TargetDirectory, "targetDirectory");
                result.Target = $"save {library.Ref.Path} as a copy in {request.TargetDirectory}";
                var started = DateTime.UtcNow.AddSeconds(-2);
                result.ReturnedType = LibraryReflection.RuntimeType(LibraryReflection.InvokeSignature(user, "SaveAs",
                    new[] { SignatureArgument.Of(new DirectoryInfo(request.TargetDirectory!)) }, "UserGlobalLibrary.SaveAs(DirectoryInfo)"));
                // The open library afterwards, found again by object identity: which path it now refers to is TIA Portal's.
                var after = LibraryResolver.OpenGlobals(portal, new List<string>()).FirstOrDefault(g => g.Raw.Equals(library.Raw));
                result.Library = after?.Ref ?? library.Ref;
                result.Note = after is null ? "SaveAs returned; the library is no longer listed among the open global libraries."
                    : LibraryPolicy.SamePath(after.Ref.Path, library.Ref.Path) ? $"SaveAs returned; the open library still refers to {after.Ref.Path}."
                    : $"SaveAs returned; the open library now refers to {after.Ref.Path} (use that path as its library selector).";
                var written = Directory.EnumerateFiles(request.TargetDirectory!, "*", SearchOption.AllDirectories)
                    .Where(file => File.GetLastWriteTimeUtc(file) >= started).Take(LibraryPolicy.MaxReferences).ToList();
                result.Transfer = new LibraryTransferInfo { Files = written.Select(Hash).ToList() };
                if (written.Count == 0) Unverified(result, $"SaveAs(...) returned, but no file written by this call was found in {request.TargetDirectory}.");
                return result;
            }
            case LibraryMethods.OpenSecondaryProject:
            {
                RequireFile(request.Path, "path");
                SecondaryProjects.RequireNotBound(project, request.Path!);
                result.Target = $"secondary project {request.Path}";
                var calls = new List<string>();
                var opened = SecondaryProjects.Open(portal, request.Path!, request.Upgrade, calls);
                result.SecondaryProject = SecondaryProjects.Describe(opened);
                result.ReturnedType = LibraryReflection.RuntimeType(opened);
                result.Note = string.Join("; ", calls) + ".";
                if (result.SecondaryProject.IsPrimary is null) Unverified(result, "The opened project's ProjectBase.IsPrimary could not be read, so its secondary mode is not verified.");
                return result;
            }
            case LibraryMethods.RetrieveSecondaryProject:
            {
                RequireFile(request.Path, "path");
                RequireDirectory(request.TargetDirectory, "targetDirectory");
                result.Target = $"secondary project from archive {request.Path}";
                var calls = new List<string>();
                var retrieved = SecondaryProjects.Retrieve(portal, request.Path!, request.TargetDirectory!, request.Upgrade, calls);
                result.SecondaryProject = SecondaryProjects.Describe(retrieved);
                result.ReturnedType = LibraryReflection.RuntimeType(retrieved);
                result.Note = string.Join("; ", calls) + ".";
                if (result.SecondaryProject.IsPrimary is null) Unverified(result, "The retrieved project's ProjectBase.IsPrimary could not be read, so its secondary mode is not verified.");
                return result;
            }
            case LibraryMethods.CloseSecondaryProject:
            {
                var secondary = SecondaryProjects.Find(portal, request.SecondaryProject, "secondaryProject");
                result.SecondaryProject = SecondaryProjects.Describe(secondary);
                result.Target = $"close secondary project {result.SecondaryProject.Path}";
                LibraryReflection.Invoke(secondary, "Close", Type.EmptyTypes, Array.Empty<object>(), "Project.Close()");
                if (SecondaryProjects.List(portal, new List<string>()).Any(entry => LibraryPolicy.SamePath(entry.Info.Path, result.SecondaryProject.Path)))
                    throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"Close() returned, but {result.SecondaryProject.Path} is still open as a secondary project.");
                return result;
            }
            default:
                return null;
        }
    }

    /// <summary>The project a create_master_copy source is taken from: the bound project, or an open secondary project (source.project).</summary>
    private static ProjectBase SourceProject(TiaPortal portal, ProjectBase bound, LibrarySourceInfo source)
        => source.Project is null ? bound : SecondaryProjects.Find(portal, source.Project, "source.project");

    /// <summary>The added content mutations; null for any other item.</summary>
    private static LibraryWriteResultInfo? MutateContentExtended(TiaPortal portal, ProjectBase project, ResolvedLibrary library, string operation,
        LibraryRequestInfo request, LibraryWriteResultInfo result)
    {
        switch (operation)
        {
            case LibraryMethods.CleanUpLibrary:
            {
                RequireWritable(library, operation);
                var (selection, description) = Selection(library, request);
                result.Target = $"clean up {description}";
                var before = ContentCount(selection);
                object? returned;
                if (LibraryPolicy.IsProject(request.Library))
                    returned = LibraryReflection.InvokeSignature(library.Raw, "CleanUpLibrary", new[]
                    {
                        SignatureArgument.Collection(LibraryPolicy.EnumTypes.LibraryTypeOrFolderSelection, new object[] { selection }),
                        SignatureArgument.Enum(LibraryPolicy.EnumTypes.CleanUpMode, request.CleanUpMode ?? LibraryPolicy.DefaultCleanUpMode),
                    }, "ProjectLibrary.CleanUpLibrary(IEnumerable<ILibraryTypeOrFolderSelection>, CleanUpMode)");
                else
                    returned = LibraryReflection.InvokeSignature(LibraryResolver.RequireUserGlobal(library, operation), "CleanUpLibrary", new[]
                    {
                        SignatureArgument.Collection(LibraryPolicy.EnumTypes.LibraryTypeOrFolderSelection, new object[] { selection }),
                    }, "UserGlobalLibrary.CleanUpLibrary(IEnumerable<ILibraryTypeOrFolderSelection>)");
                result.ReturnedType = LibraryReflection.RuntimeType(returned);
                string after;
                try
                {
                    after = ContentCount(selection);
                }
                catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
                {
                    after = "the selection can no longer be read (a deleted type)";
                }

                result.Note = $"Before: {before}. After: {after}.";
                return result;
            }
            case LibraryMethods.HarmonizeProject:
            {
                var (selection, description) = Selection(library, request);
                var plc = PlcSoftwareLocator.FindUnique(project, request.PlcName);
                result.Target = $"harmonize PLC '{plc.Software.Name}' from {description}";
                result.ReturnedType = LibraryReflection.RuntimeType(LibraryReflection.InvokeSignature(library.Raw, "HarmonizeProject", new[]
                {
                    SignatureArgument.Collection(LibraryPolicy.EnumTypes.LibraryTypeOrFolderSelection, new object[] { selection }),
                    SignatureArgument.Collection(LibraryPolicy.EnumTypes.UpdateProjectScope, new object[] { Scope(plc) }),
                    SignatureArgument.Enum(LibraryPolicy.EnumTypes.HarmonizeProjectOptions, string.Join(", ", request.HarmonizeOptions ?? new List<string>())),
                }, "HarmonizeProject(IEnumerable<ILibraryTypeOrFolderSelection>, IEnumerable<IUpdateProjectScope>, HarmonizeProjectOptions)"));
                result.Note = "HarmonizeProject returns nothing to read back; inspect the PLC with plc_read and run compile_check.";
                return result;
            }
            case LibraryMethods.UpdateLibrary:
            {
                var (selection, description) = Selection(library, request);
                var target = LibraryResolver.Resolve(portal, project, request.TargetLibrary);
                RequireWritable(target, operation);
                result.Target = $"update {target.Ref.Library} from {description}";
                var arguments = new List<SignatureArgument>
                {
                    SignatureArgument.Collection(LibraryPolicy.EnumTypes.LibraryTypeOrFolderSelection, new object[] { selection }),
                    SignatureArgument.Of(target.Library, typeof(ILibrary)),
                };
                arguments.AddRange(ModeArguments(request, all: true));
                result.ReturnedType = LibraryReflection.RuntimeType(LibraryReflection.InvokeSignature(library.Raw, "UpdateLibrary", arguments,
                    arguments.Count == 2 ? "UpdateLibrary(IEnumerable<ILibraryTypeOrFolderSelection>, ILibrary)"
                        : "UpdateLibrary(IEnumerable<ILibraryTypeOrFolderSelection>, ILibrary, ForceUpdateMode, DeleteUnusedVersionsMode, StructureConflictResolutionMode)"));
                result.Note = $"Inspect {target.Ref.Library} with library_read get_library_tree.";
                return result;
            }
            case LibraryMethods.DeleteType:
            {
                RequireWritable(library, operation);
                var (type, path) = LibraryResolver.ResolveType(library.Library, request.TypePath, request.TypeGuid);
                var guid = type.Guid;
                result.Target = $"type {path ?? guid.ToString()}";
                result.ReturnedType = LibraryReflection.RuntimeType(LibraryReflection.Invoke(type, "Delete", Type.EmptyTypes, Array.Empty<object>(), "LibraryType.Delete()"));
                if (library.Library.FindType(guid) is not null)
                    throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"Delete() returned, but type {guid} is still found (ILibrary.FindType).");
                return result;
            }
            case LibraryMethods.SetTypeDoNotUse:
            {
                RequireWritable(library, operation);
                var (type, path) = LibraryResolver.ResolveType(library.Library, request.TypePath, request.TypeGuid);
                result.Target = $"type {path ?? type.Guid.ToString()}";
                var after = LibraryReflection.SetProperty(type, "DoNotUse", request.DoNotUse!.Value, "LibraryType.DoNotUse");
                if (after is not bool value || value != request.DoNotUse.Value)
                    throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"LibraryType.DoNotUse reads {after ?? "null"} after it was set to {request.DoNotUse}.");
                result.Note = $"LibraryType.DoNotUse reads {value}.";
                return result;
            }
            case LibraryMethods.CreateTypeFolder:
            {
                RequireWritable(library, operation);
                var segments = LibraryResolver.Segments(request.FolderPath);
                var parent = LibraryResolver.ResolveFolder(library.Library, segments);
                result.Target = $"new type folder {LibraryPolicy.FormatPath(segments.Concat(new[] { request.NewName! }))}";
                var composition = LibraryReflection.Required(parent, typeof(LibraryTypeFolder), "Folders", "LibraryTypeFolder.Folders")!;
                var created = LibraryReflection.InvokeSignature(composition, "Create", new[] { SignatureArgument.Of(request.NewName!) },
                    "LibraryTypeUserFolderComposition.Create(String)");
                result.ReturnedType = LibraryReflection.RuntimeType(created);
                if (!LibraryResolver.ChildFolders(parent).Any(child => string.Equals(LibraryResolver.FolderName(child, new List<string>()), request.NewName, StringComparison.Ordinal)))
                    throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"Create(\"{request.NewName}\") returned, but the folder is not listed below {LibraryPolicy.FormatPath(segments)}.");
                result.Created.Add(new LibraryCreatedObjectInfo { Name = request.NewName, RuntimeType = result.ReturnedType });
                result.NameAfter = request.NewName;
                return result;
            }
            case LibraryMethods.DeleteTypeFolder:
            {
                RequireWritable(library, operation);
                var segments = UserFolder(request.FolderPath, "type");
                var folder = LibraryResolver.ResolveFolder(library.Library, segments);
                var parent = LibraryResolver.ResolveFolder(library.Library, segments.Take(segments.Count - 1).ToArray());
                result.Target = $"type folder {LibraryPolicy.FormatPath(segments)}";
                result.ReturnedType = LibraryReflection.RuntimeType(LibraryReflection.Invoke(folder, "Delete", Type.EmptyTypes, Array.Empty<object>(), "LibraryTypeUserFolder.Delete()"));
                if (LibraryResolver.ChildFolders(parent).Any(child => string.Equals(LibraryResolver.FolderName(child, new List<string>()), segments[segments.Count - 1], StringComparison.OrdinalIgnoreCase)))
                    throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"Delete() returned, but type folder {LibraryPolicy.FormatPath(segments)} is still listed.");
                return result;
            }
            case LibraryMethods.CreateMasterCopyFolder:
            {
                RequireWritable(library, operation);
                var segments = LibraryResolver.Segments(request.MasterCopyFolderPath);
                var parent = LibraryResolver.ResolveMasterCopyFolder(library.Library, segments);
                result.Target = $"new master copy folder {LibraryPolicy.FormatPath(segments.Concat(new[] { request.NewName! }))}";
                var created = LibraryReflection.InvokeSignature(LibraryResolver.MasterCopyFolderComposition(parent), "Create",
                    new[] { SignatureArgument.Of(request.NewName!) }, "MasterCopyUserFolderComposition.Create(String)");
                result.ReturnedType = LibraryReflection.RuntimeType(created);
                if (!LibraryResolver.MasterCopyChildFolders(parent).Any(child => string.Equals(LibraryResolver.MasterCopyFolderName(child, new List<string>()), request.NewName, StringComparison.Ordinal)))
                    throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"Create(\"{request.NewName}\") returned, but the master copy folder is not listed below {LibraryPolicy.FormatPath(segments)}.");
                result.Created.Add(new LibraryCreatedObjectInfo { Name = request.NewName, RuntimeType = result.ReturnedType });
                result.NameAfter = request.NewName;
                return result;
            }
            case LibraryMethods.DeleteMasterCopyFolder:
            {
                RequireWritable(library, operation);
                var segments = UserFolder(request.MasterCopyFolderPath, "master copy");
                var folder = LibraryResolver.ResolveMasterCopyFolder(library.Library, segments);
                var parent = LibraryResolver.ResolveMasterCopyFolder(library.Library, segments.Take(segments.Count - 1).ToArray());
                result.Target = $"master copy folder {LibraryPolicy.FormatPath(segments)}";
                result.ReturnedType = LibraryReflection.RuntimeType(LibraryReflection.Invoke(folder, "Delete", Type.EmptyTypes, Array.Empty<object>(), "MasterCopyUserFolder.Delete()"));
                if (LibraryResolver.MasterCopyChildFolders(parent).Any(child => string.Equals(LibraryResolver.MasterCopyFolderName(child, new List<string>()), segments[segments.Count - 1], StringComparison.OrdinalIgnoreCase)))
                    throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"Delete() returned, but master copy folder {LibraryPolicy.FormatPath(segments)} is still listed.");
                return result;
            }
            case LibraryMethods.RenameMasterCopyFolder:
            {
                RequireWritable(library, operation);
                var segments = UserFolder(request.MasterCopyFolderPath, "master copy");
                var folder = LibraryResolver.ResolveMasterCopyFolder(library.Library, segments);
                result.Target = $"master copy folder {LibraryPolicy.FormatPath(segments)}";
                result.NameAfter = LibraryReflection.Rename(folder, request.NewName!, $"Master copy folder '{LibraryPolicy.FormatPath(segments)}'");
                return result;
            }
            case LibraryMethods.CreateTypeFromDocuments:
            {
                RequireWritable(library, operation);
                RequireDirectory(request.DocumentDirectory, "documentDirectory");
                var segments = LibraryResolver.Segments(request.FolderPath);
                var folder = LibraryResolver.ResolveFolder(library.Library, segments);
                result.Target = $"new type from documents '{request.DocumentName}' in type folder {LibraryPolicy.FormatPath(segments)}";
                var composition = LibraryReflection.Required(folder, typeof(LibraryTypeFolder), "Types", "LibraryTypeFolder.Types")!;
                var arguments = new List<SignatureArgument> { SignatureArgument.Of(new DirectoryInfo(request.DocumentDirectory!)), SignatureArgument.Of(request.DocumentName!) };
                if (TargetEnvironment(project, request) is { } environment) arguments.Add(SignatureArgument.Of(environment.Object, typeof(IEngineeringObject)));
                arguments.Add(SignatureArgument.Enum(LibraryPolicy.EnumTypes.LibraryImportOptions, request.LibraryImportOptions ?? LibraryPolicy.DefaultLibraryImportOptions));
                var returned = LibraryReflection.InvokeSignature(composition, "CreateFromDocuments", arguments,
                    arguments.Count == 4 ? "LibraryTypeComposition.CreateFromDocuments(DirectoryInfo, String, IEngineeringObject, LibraryImportOptions)"
                        : "LibraryTypeComposition.CreateFromDocuments(DirectoryInfo, String, LibraryImportOptions)");
                result.ReturnedType = LibraryReflection.RuntimeType(returned);
                result.Transfer = Transfer(returned, "TypeCreateTransferResults");
                var createdType = LibraryReflection.Optional(returned, "CreatedType", result.Transfer.Unreadable, "TypeCreateTransferResults.CreatedType");
                if (returned is null)
                    throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, "CreateFromDocuments returned nothing; inspect the type folder with library_read get_library_tree.");
                if (createdType is LibraryType type)
                {
                    var name = LibraryResolver.SafeName(type);
                    result.Created.Add(new LibraryCreatedObjectInfo { Name = name, RuntimeType = LibraryReflection.RuntimeType(type) });
                    if (library.Library.FindType(type.Guid) is null)
                        throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"CreateFromDocuments reported type '{name}', but ILibrary.FindType does not find it.");
                }
                else Unverified(result, "CreateFromDocuments returned, but TypeCreateTransferResults.CreatedType could not be read as a library type, so the created type is not read back.");
                return result;
            }
            case LibraryMethods.CreateVersionFromDocuments:
            {
                RequireWritable(library, operation);
                RequireDirectory(request.DocumentDirectory, "documentDirectory");
                var (type, path) = LibraryResolver.ResolveType(library.Library, request.TypePath, request.TypeGuid);
                result.Target = $"new version of type {path ?? type.Guid.ToString()} from documents '{request.DocumentName}'";
                var composition = LibraryReflection.Required(type, "Versions", "LibraryType.Versions")!;
                var arguments = new List<SignatureArgument> { SignatureArgument.Of(new DirectoryInfo(request.DocumentDirectory!)), SignatureArgument.Of(request.DocumentName!) };
                if (TargetEnvironment(project, request) is { } environment) arguments.Add(SignatureArgument.Of(environment.Object, typeof(IEngineeringObject)));
                arguments.Add(SignatureArgument.Enum(LibraryPolicy.EnumTypes.CreateOptions, request.CreateOption ?? LibraryPolicy.DefaultCreateOption));
                arguments.Add(SignatureArgument.Enum(LibraryPolicy.EnumTypes.LibraryImportOptions, request.LibraryImportOptions ?? LibraryPolicy.DefaultLibraryImportOptions));
                var returned = LibraryReflection.InvokeSignature(composition, "CreateFromDocuments", arguments,
                    arguments.Count == 5 ? "LibraryTypeVersionComposition.CreateFromDocuments(DirectoryInfo, String, IEngineeringObject, CreateOptions, LibraryImportOptions)"
                        : "LibraryTypeVersionComposition.CreateFromDocuments(DirectoryInfo, String, CreateOptions, LibraryImportOptions)");
                result.ReturnedType = LibraryReflection.RuntimeType(returned);
                if (returned is null)
                    throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, "CreateFromDocuments returned nothing; inspect the type with library_read get_type.");
                result.Transfer = Transfer(returned, "VersionCreateTransferResults");
                if (LibraryReflection.Optional(returned, "CreatedVersion", result.Transfer.Unreadable, "VersionCreateTransferResults.CreatedVersion") is LibraryTypeVersion version)
                {
                    ReadBack(version, "CreateFromDocuments", result);
                    if (library.Library.FindVersion(version.Guid) is null)
                        throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"CreateFromDocuments reported version {version.Guid}, but ILibrary.FindVersion does not find it.");
                }
                else Unverified(result, "CreateFromDocuments returned, but VersionCreateTransferResults.CreatedVersion could not be read as a type version, so the created version is not read back.");
                return result;
            }
            case LibraryMethods.ExportTypeVersion:
                return ExportVersion(library, request, result);
            case LibraryMethods.ExportTypeVersionAsDocuments:
            {
                var (_, path, version) = LibraryResolver.ResolveVersion(library.Library, request);
                RequireDirectory(request.DocumentDirectory, "documentDirectory");
                _ = RequireExportNames(request);
                result.Target = $"type {path ?? request.TypeGuid ?? request.VersionGuid} version {LibraryResolver.VersionText(version, new List<string>())} as {request.ExportFormat} documents";
                var started = DateTime.UtcNow.AddSeconds(-2);
                var returned = LibraryReflection.InvokeSignature(version, "ExportAsDocuments", new[]
                {
                    SignatureArgument.Of(new DirectoryInfo(request.DocumentDirectory!)), SignatureArgument.Of(request.DocumentName!),
                    SignatureArgument.Of(request.ExportFormat!), SignatureArgument.Enum(LibraryPolicy.EnumTypes.LibraryExportOptions,
                        request.LibraryExportOptions ?? LibraryPolicy.DefaultLibraryExportOptions),
                }, "LibraryTypeVersion.ExportAsDocuments(DirectoryInfo, String, String, LibraryExportOptions)");
                result.ReturnedType = LibraryReflection.RuntimeType(returned);
                result.Transfer = Transfer(returned, "ExportTransferResult");
                result.Transfer.Documents = LibraryReflection.Enumerate(LibraryReflection.Optional(returned, "ExportedDocuments", result.Transfer.Unreadable,
                        "ExportTransferResult.ExportedDocuments"), "exported documents").Take(LibraryPolicy.MaxReferences)
                    .Select(document => LibraryReflection.Display(document) ?? string.Empty).ToList();
                var written = Matching(request.DocumentDirectory!, request.DocumentName!).Where(file => File.GetLastWriteTimeUtc(file) >= started).ToList();
                result.Transfer.Files = written.Select(Hash).ToList();
                if (written.Count == 0)
                    Unverified(result, $"ExportAsDocuments returned, but no file named '{request.DocumentName}' written by this call was found in {request.DocumentDirectory}.");
                return result;
            }
            default:
                return null;
        }
    }

    /// <summary>update_project with the mode overloads: the project library's (…, DeleteUnusedVersionsMode), a global library's (…, Force, DeleteUnused, Structure).</summary>
    private static object? UpdateProjectWithModes(ResolvedLibrary library, ILibraryTypeOrFolderSelection selection, IUpdateProjectScope scope, LibraryRequestInfo request)
    {
        var arguments = new List<SignatureArgument>
        {
            SignatureArgument.Collection(LibraryPolicy.EnumTypes.LibraryTypeOrFolderSelection, new object[] { selection }),
            SignatureArgument.Collection(LibraryPolicy.EnumTypes.UpdateProjectScope, new object[] { scope }),
        };
        var project = LibraryPolicy.IsProject(request.Library);
        arguments.AddRange(ModeArguments(request, all: !project));
        return LibraryReflection.InvokeSignature(library.Raw, "UpdateProject", arguments, project
            ? "ProjectLibrary.UpdateProject(IEnumerable<ILibraryTypeOrFolderSelection>, IEnumerable<IUpdateProjectScope>, DeleteUnusedVersionsMode)"
            : "GlobalLibrary.UpdateProject(IEnumerable<ILibraryTypeOrFolderSelection>, IEnumerable<IUpdateProjectScope>, ForceUpdateMode, DeleteUnusedVersionsMode, StructureConflictResolutionMode)");
    }

    private static bool HasModes(LibraryRequestInfo request)
        => request.ForceUpdateMode is not null || request.DeleteUnusedVersionsMode is not null || request.StructureConflictResolutionMode is not null;

    private static IEnumerable<SignatureArgument> ModeArguments(LibraryRequestInfo request, bool all)
    {
        if (!HasModes(request)) yield break;
        if (all)
        {
            if (request.ForceUpdateMode is null || request.DeleteUnusedVersionsMode is null || request.StructureConflictResolutionMode is null)
                throw LibraryResolver.Validation("give forceUpdateMode, deleteUnusedVersionsMode and structureConflictResolutionMode together or none.");
            yield return SignatureArgument.Enum(LibraryPolicy.EnumTypes.ForceUpdateMode, request.ForceUpdateMode);
            yield return SignatureArgument.Enum(LibraryPolicy.EnumTypes.DeleteUnusedVersionsMode, request.DeleteUnusedVersionsMode);
            yield return SignatureArgument.Enum(LibraryPolicy.EnumTypes.StructureConflictResolutionMode, request.StructureConflictResolutionMode);
        }
        else
        {
            if (request.ForceUpdateMode is not null || request.StructureConflictResolutionMode is not null || request.DeleteUnusedVersionsMode is null)
                throw LibraryResolver.Validation("the project library's UpdateProject takes deleteUnusedVersionsMode only.");
            yield return SignatureArgument.Enum(LibraryPolicy.EnumTypes.DeleteUnusedVersionsMode, request.DeleteUnusedVersionsMode);
        }
    }

    private static string? Modes(LibraryRequestInfo request)
        => HasModes(request)
            ? string.Join(", ", new[] { ("ForceUpdateMode", request.ForceUpdateMode), ("DeleteUnusedVersionsMode", request.DeleteUnusedVersionsMode),
                ("StructureConflictResolutionMode", request.StructureConflictResolutionMode) }.Where(pair => pair.Item2 is not null).Select(pair => $"{pair.Item1} {pair.Item2}"))
            : null;

    /// <summary>export_type_version: <c>Export(FileInfo, ExportOptions[, DocumentInfoOptions])</c> into a staged file moved into place, then re-hashed.</summary>
    private static LibraryWriteResultInfo ExportVersion(ResolvedLibrary library, LibraryRequestInfo request, LibraryWriteResultInfo result)
    {
        var (_, path, version) = LibraryResolver.ResolveVersion(library.Library, request);
        var target = ExportFile(request);
        result.Target = $"type {path ?? request.TypeGuid ?? request.VersionGuid} version {LibraryResolver.VersionText(version, new List<string>())} to {target}";
        var staged = Path.Combine(Path.GetDirectoryName(target)!, $".{Path.GetFileNameWithoutExtension(target)}.{Guid.NewGuid():N}.tmp{Path.GetExtension(target)}");
        var arguments = new List<SignatureArgument>
        {
            SignatureArgument.Of(new FileInfo(staged)),
            SignatureArgument.Enum(LibraryPolicy.EnumTypes.ExportOptions, request.ExportOptions ?? LibraryPolicy.DefaultExportOptions),
        };
        if (request.DocumentInfoOptions is not null) arguments.Add(SignatureArgument.Enum(LibraryPolicy.EnumTypes.DocumentInfoOptions, request.DocumentInfoOptions));
        try
        {
            result.ReturnedType = LibraryReflection.RuntimeType(LibraryReflection.InvokeSignature(version, "Export", arguments,
                arguments.Count == 3 ? "LibraryTypeVersion.Export(FileInfo, ExportOptions, DocumentInfoOptions)" : "LibraryTypeVersion.Export(FileInfo, ExportOptions)"));
            if (!File.Exists(staged))
                throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, "Export(...) returned, but wrote no file.");
            if (File.Exists(target))
            {
                if (!request.Overwrite) throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"filePath '{target}' appeared during the export; set overwrite:true to replace it.");
                File.Replace(staged, target, null);
            }
            else File.Move(staged, target);
        }
        finally
        {
            if (File.Exists(staged)) File.Delete(staged);
        }

        result.Transfer = new LibraryTransferInfo { Files = new List<LibraryFileInfo> { Hash(target) } };
        return result;
    }

    // ================================================================ helpers

    private static string Archivation(LibraryRequestInfo request) => request.ArchivationMode ?? LibraryPolicy.DefaultArchivationMode;

    /// <summary>The non-root folder path of a delete or rename (the root folder cannot be deleted or renamed).</summary>
    private static IReadOnlyList<string> UserFolder(string? folderPath, string kind)
    {
        var segments = LibraryResolver.Segments(folderPath);
        if (segments.Count == 0) throw LibraryResolver.Validation($"name a {kind} user folder; the root {kind} folder cannot be deleted or renamed.");
        return segments;
    }

    /// <summary>The PLC whose software is the documented <c>targetEnvironment</c> of a create-from-documents; null without plcName.</summary>
    private static (string Name, IEngineeringObject Object)? TargetEnvironment(ProjectBase project, LibraryRequestInfo request)
    {
        if (request.PlcName is null) return null;
        var plc = PlcSoftwareLocator.FindUnique(project, request.PlcName);
        return (plc.Software.Name, plc.Software as IEngineeringObject ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
            $"PLC '{plc.Software.Name}' is not an IEngineeringObject on this TIA Portal installation."));
    }

    private static string DocumentsText(string directory, string name)
    {
        var found = Matching(directory, name);
        return found.Count == 0 ? $"No file named '{name}' is in {directory}; TIA Portal decides which documents it needs."
            : $"Documents found: {string.Join(", ", found.Select(Path.GetFileName))}.";
    }

    /// <summary>Files in <paramref name="directory"/> named <paramref name="name"/> or <c>name.*</c> (top level).</summary>
    private static List<string> Matching(string directory, string name)
        => Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory).Where(file => string.Equals(Path.GetFileName(file), name, StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileName(file).StartsWith(name + ".", StringComparison.OrdinalIgnoreCase))
                .Take(LibraryPolicy.MaxReferences).ToList()
            : new List<string>();

    /// <summary>The export target of export_type_version: a full path whose directory exists; an existing file needs overwrite:true.</summary>
    private static string ExportFile(LibraryRequestInfo request)
    {
        if (string.IsNullOrWhiteSpace(request.FilePath)) throw LibraryResolver.Validation("filePath is required.");
        if (LibraryPolicy.FilePathError(request.FilePath) is { } error) throw LibraryResolver.Validation($"filePath {error}");
        var full = Path.GetFullPath(request.FilePath);
        if (!Directory.Exists(Path.GetDirectoryName(full)))
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"The directory of filePath '{full}' does not exist.");
        if (Directory.Exists(full)) throw LibraryResolver.Validation($"filePath '{full}' is a directory.");
        if (File.Exists(full) && !request.Overwrite) throw LibraryResolver.Validation($"filePath '{full}' exists; set overwrite:true to replace it.");
        return full;
    }

    /// <summary>Existing documents of export_type_version_as_documents: refused without overwrite:true.</summary>
    private static List<string> RequireExportNames(LibraryRequestInfo request)
    {
        var existing = Matching(request.DocumentDirectory!, request.DocumentName!);
        if (existing.Count > 0 && !request.Overwrite)
            throw LibraryResolver.Validation($"{request.DocumentDirectory} already holds {string.Join(", ", existing.Select(Path.GetFileName))}; set overwrite:true to export anyway.");
        return existing;
    }

    private static LibraryFileInfo Hash(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return new LibraryFileInfo { Path = path, Bytes = new FileInfo(path).Length, Sha256 = string.Concat(hash.Select(b => b.ToString("x2"))) };
    }

    /// <summary>State and messages of a transfer result (TransferResultState, Messages[].Message: runtime lookups).</summary>
    private static LibraryTransferInfo Transfer(object? returned, string kind)
    {
        var transfer = new LibraryTransferInfo();
        if (returned is null) return transfer;
        transfer.State = LibraryReflection.Display(LibraryReflection.Optional(returned, "TransferResultState", transfer.Unreadable, kind + ".TransferResultState"));
        transfer.Messages = LibraryReflection.Enumerate(LibraryReflection.Optional(returned, "Messages", transfer.Unreadable, kind + ".Messages"), "transfer messages")
            .Take(LibraryPolicy.MaxMessages)
            .Select(message => LibraryReflection.Display(LibraryReflection.Optional(message, "Message", transfer.Unreadable, "TransferResultMessage.Message")) ?? string.Empty)
            .ToList();
        transfer.Unreadable = transfer.Unreadable.Distinct(StringComparer.Ordinal).ToList();
        return transfer;
    }

    /// <summary>Types and versions below a selection (a type or a type folder), for the clean-up evidence.</summary>
    private static string ContentCount(ILibraryTypeOrFolderSelection selection)
    {
        var types = 0;
        var versions = 0;
        void Count(LibraryType type)
        {
            types++;
            versions += LibraryResolver.Versions(type).Count;
        }

        void Walk(LibraryTypeFolder folder, int depth)
        {
            foreach (var type in LibraryResolver.Types(folder)) Count(type);
            if (depth < LibraryPolicy.MaxTreeDepth) foreach (var child in LibraryResolver.ChildFolders(folder)) Walk(child, depth + 1);
        }

        if (selection is LibraryType single) Count(single);
        else if (selection is LibraryTypeFolder folder) Walk(folder, 1);
        return $"{types} type(s) with {versions} version(s) in the selection";
    }
}
