using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.FakeWorker.Library;

/// <summary>
/// The issue #17 operations of the <c>library-roundtrip</c> fixture. Every type supports the export formats <c>SimaticML</c> and
/// <c>SimaticSD</c>; compares list one identical and one different property. Directory <c>C:\Libraries\Docs</c> holds documents
/// named <c>NewType</c> (create_type_from_documents creates a code-block type of that name) and <c>PumpControl</c>
/// (create_version_from_documents creates the next version); <c>C:\Libraries\Export</c> takes exports. A clean-up removes every
/// in-work version that has no instance. Harmonize and update_library change nothing observable. This models object access only;
/// it is no evidence of Siemens behavior.
/// </summary>
sealed partial class LibraryScenario
{
    private const string DocumentsDirectory = @"C:\Libraries\Docs";

    /// <summary>The project file opened as a secondary project (issue #20); its PLC_OLD holds the block <c>LegacyFB</c>.</summary>
    private const string SecondaryProjectPath = @"C:\Projects\Old\Old.ap21";
    private const string SecondaryArchivePath = @"C:\Projects\Archive\Old.zap21";

    private readonly List<string> _secondary = new();

    /// <summary>
    /// TIA Portal settings (issue #27): folder <c>General</c> with <c>UserInterfaceLanguage</c> (text) and <c>ShowStartView</c> (bool);
    /// folder <c>General/Security</c> with <c>CertificateCheck</c> (bool, security-relevant). <c>ReadOnlyInfo</c> has no setter.
    /// </summary>
    private readonly Dictionary<string, (object Value, bool Writable)> _settings = new(StringComparer.Ordinal)
    {
        ["General\nUserInterfaceLanguage"] = ("en-US", true),
        ["General\nShowStartView"] = (true, true),
        ["General\nReadOnlyInfo"] = ("V21", false),
        ["General/Security\nCertificateCheck"] = (true, true),
        ["General/Accept\nAutoAcceptCertificates"] = (false, true),
        ["Drift\nOption"] = (false, true),
    };

    private int _driftPlans;

    /// <summary>Caller paths that TIA Portal (the model) resolves to another folder: a resolved name differing from the caller's text.</summary>
    private readonly Dictionary<string, string> _aliases = new(StringComparer.Ordinal)
    {
        ["Display\nAutoAcceptCertificates"] = "General/Accept\nAutoAcceptCertificates",
    };
    private const string ExportDirectory = @"C:\Libraries\Export";

    private object ReadExtended(string operation, LibraryRequestInfo request)
    {
        switch (operation)
        {
            case LibraryMethods.GetSupportedExportFormats:
            {
                var library = Resolve(request.Library);
                var (type, path) = Type(library, request);
                return new LibraryExportFormatsInfo
                {
                    Library = library.Ref, Path = path, Name = type.Name, Guid = type.Guid.ToString(), Formats = new List<string> { "SimaticML", "SimaticSD" },
                };
            }
            case LibraryMethods.CompareTypes:
            case LibraryMethods.CompareTypeVersions:
            case LibraryMethods.CompareMasterCopies:
            {
                var library = Resolve(request.Library);
                var other = request.TargetLibrary is null ? library : Resolve(request.TargetLibrary);
                string left, right;
                if (operation == LibraryMethods.CompareMasterCopies)
                {
                    left = "master copy " + MasterCopy(library, request.MasterCopyName, request.MasterCopyFolderPath).Name;
                    right = "master copy " + MasterCopy(other, request.TargetMasterCopyName, request.TargetMasterCopyFolderPath).Name;
                }
                else
                {
                    var target = new LibraryRequestInfo
                    {
                        TypePath = request.TargetTypePath, TypeGuid = request.TargetTypeGuid, Version = request.TargetVersion, VersionGuid = request.TargetVersionGuid,
                    };
                    if (operation == LibraryMethods.CompareTypes)
                    {
                        left = "type " + Type(library, request).Path;
                        right = "type " + Type(other, target).Path;
                    }
                    else
                    {
                        var (_, leftPath, leftVersion) = Version(library, request, allowAll: false);
                        var (_, rightPath, rightVersion) = Version(other, target, allowAll: false);
                        left = $"type {leftPath} version {leftVersion!.Number}";
                        right = $"type {rightPath} version {rightVersion!.Number}";
                    }
                }

                if (library == other && left == right) throw new Refusal(WorkerFailureCategories.ValidationError, "the compared object is the object itself; name another one.");
                return new LibraryDetailedCompareInfo
                {
                    Library = library.Ref, Left = left, CompareTo = other.Ref, Right = right, Complete = true,
                    Properties = new List<LibraryDetailedCompareEntryInfo>
                    {
                        new() { Description = "Name", Status = "ObjectsDifferent", LeftValue = left, RightValue = right },
                        new() { Description = "Author", Status = "ObjectsIdentical", LeftValue = "Fake", RightValue = "Fake" },
                    }.Take(request.MaxElements).ToList(),
                };
            }
            case LibraryMethods.ListPortalSettings:
            {
                var start = (request.SettingsFolderPath ?? string.Empty).Trim('/');
                var settings = _settings.Where(pair => start.Length == 0 || pair.Key.StartsWith(start + "\n", StringComparison.Ordinal) || pair.Key.StartsWith(start + "/", StringComparison.Ordinal))
                    .Select(pair => Setting(pair.Key, pair.Value)).ToList();
                if (start.Length > 0 && settings.Count == 0) throw new Refusal(WorkerFailureCategories.TargetNotFound, $"TIA Portal settings folder '{start}' was not found.");
                return new PortalSettingsInfo
                {
                    FolderPath = start, MaxDepth = request.MaxDepth, Settings = settings,
                    Folders = settings.Select(s => s.FolderPath).Distinct().Where(f => f != start).ToList(),
                };
            }
            case LibraryMethods.ListSecondaryProjects:
                return new SecondaryProjectListInfo
                {
                    Projects = _secondary.Select(path => new SecondaryProjectInfo { Path = path, Name = System.IO.Path.GetFileNameWithoutExtension(path), IsPrimary = false }).ToList(),
                };
            default:
                throw new Refusal(WorkerFailureCategories.ValidationError, $"unexpected library read '{operation}'");
        }
    }

    /// <summary>The secondary-project lifecycle items (plan and write); null for the other items.</summary>
    private LibraryWriteResultInfo? SecondaryProjectItem(LibraryRequestInfo request, LibraryPlanInfo? plan, LibraryWriteResultInfo? result)
    {
        string Path() => request.Operation == LibraryMethods.RetrieveSecondaryProject ? @"C:\Projects\Retrieved\Old\Old.ap21" : request.Path!;
        switch (request.Operation)
        {
            case LibraryMethods.OpenSecondaryProject:
            case LibraryMethods.RetrieveSecondaryProject:
                if (request.Path != (request.Operation == LibraryMethods.OpenSecondaryProject ? SecondaryProjectPath : SecondaryArchivePath))
                    throw new Refusal(WorkerFailureCategories.TargetNotFound, $"path '{request.Path}' does not exist.");
                if (plan is not null)
                {
                    plan.Target = $"secondary project {Path()} (ProjectOpenMode.Secondary)";
                    plan.Consequences.Add("Opens the project read-only as a secondary (reference) project next to the bound project; the binding does not change.");
                    return null;
                }

                _secondary.Add(Path());
                result!.Target = $"secondary project {Path()}";
                result.SecondaryProject = new SecondaryProjectInfo { Path = Path(), Name = "Old", IsPrimary = false };
                return result;
            case LibraryMethods.CloseSecondaryProject:
                if (!_secondary.Any(p => LibraryPolicy.SamePath(p, request.SecondaryProject)))
                    throw new Refusal(WorkerFailureCategories.TargetNotFound, $"No secondary project with the path '{request.SecondaryProject}' is open (list_secondary_projects).");
                if (plan is not null)
                {
                    plan.Target = $"close secondary project {request.SecondaryProject}";
                    return null;
                }

                _secondary.RemoveAll(p => LibraryPolicy.SamePath(p, request.SecondaryProject));
                result!.Target = $"close secondary project {request.SecondaryProject}";
                result.SecondaryProject = new SecondaryProjectInfo { Path = request.SecondaryProject, Name = "Old", IsPrimary = false };
                return result;
            default:
                throw new Refusal(WorkerFailureCategories.ValidationError, $"unexpected secondary project item '{request.Operation}'");
        }
    }

    private static PortalSettingInfo Setting(string key, (object Value, bool Writable) entry)
    {
        var parts = key.Split('\n');
        return new PortalSettingInfo
        {
            FolderPath = parts[0], Name = parts[1], Value = entry.Value is bool b ? (b ? "true" : "false") : entry.Value.ToString(),
            ValueType = entry.Value.GetType().FullName, Writable = entry.Writable, SecurityRelevant = LibraryPolicy.IsSecurityRelevantSetting(parts[0], parts[1]),
        };
    }

    /// <summary>set_portal_setting (plan and write).</summary>
    private object PortalSettingItem(LibraryRequestInfo request, bool mutate, LibraryWriteResultInfo? result)
    {
        var key = (request.SettingsFolderPath ?? string.Empty).Trim('/') + "\n" + request.SettingName;
        // TIA Portal resolves folders by Find(String); the model resolves the folder alias "Display" to "General/Accept"
        // (a resolved name that differs from the caller's text) and, after a planning pass, flips the alias "Drift" (F2 tests).
        var callerKey = key;
        if (_aliases.TryGetValue(key, out var aliased)) key = aliased;
        // "Drift/Option" changes after the second planning pass (the pipeline's re-plan after the confirmation), so only the
        // worker's own check at the change can see it.
        if (!mutate && callerKey == "Drift\nOption" && ++_driftPlans == 2) _aliases[callerKey] = "General/Accept\nAutoAcceptCertificates";
        if (!_settings.TryGetValue(key, out var entry)) throw new Refusal(WorkerFailureCategories.TargetNotFound, $"TIA Portal setting '{request.SettingName}' was not found in the folder.");
        var current = Setting(key, entry);
        if (!mutate)
            return new LibraryPlanInfo
            {
                Operation = request.Operation, Target = $"TIA Portal setting {current.FolderPath}/{current.Name} = {current.Value} ({current.ValueType}) -> {request.SettingValue}",
                Consequences = { "The setting applies to this TIA Portal instance, not to the project; it is not saved with the project." },
                Setting = current,
            };
        // Like the worker: the setting is resolved again and a classification that differs from the plan is refused (F2).
        if (LibraryPolicy.PortalSettingClassificationMismatch(request.PlannedSecurityRelevant, current.SecurityRelevant, $"TIA Portal setting '{current.FolderPath}/{current.Name}'") is { } mismatch)
            throw new Refusal(WorkerFailureCategories.StateChanged, mismatch);
        if (!entry.Writable)
            throw new Refusal(WorkerFailureCategories.UnsupportedCapability, "This TIA Portal installation exposes no public setter of TiaPortalSetting.Value. Nothing was changed.");
        object value = entry.Value is bool
            ? request.SettingValue switch { "true" => true, "false" => false, _ => throw new Refusal(WorkerFailureCategories.ValidationError, $"settingValue '{request.SettingValue}' is no Boolean.") }
            : request.SettingValue ?? string.Empty;
        _settings[key] = (value, true);
        result!.Setting = new PortalSettingChangeInfo
        {
            FolderPath = current.FolderPath, Name = current.Name!, Before = current.Value, After = Setting(key, _settings[key]).Value, ValueType = current.ValueType,
            SecurityRelevant = current.SecurityRelevant,
        };
        result.Target = $"TIA Portal setting {current.FolderPath}/{current.Name}";
        return result;
    }

    /// <summary>A create_master_copy source in a secondary project: it must be open and hold the block.</summary>
    private void RequireSecondarySource(LibrarySourceInfo source)
    {
        if (!_secondary.Any(p => LibraryPolicy.SamePath(p, source.Project)))
            throw new Refusal(WorkerFailureCategories.TargetNotFound, $"No secondary project with the path '{source.Project}' is open (list_secondary_projects).");
        if (source.Kind != LibraryPolicy.SourceKinds.Block || source.Path.Split('/').Last() != "LegacyFB")
            throw new Refusal(WorkerFailureCategories.TargetNotFound, $"'{source.Path}' was not found in the secondary project.");
    }

    /// <summary>The issue #17 content items (plan and write); null for the other items.</summary>
    private ContentOutcome? ContentExtended(FakeLibrary library, LibraryRequestInfo request, List<string> consequences, bool mutate)
    {
        var created = new List<LibraryCreatedObjectInfo>();
        switch (request.Operation)
        {
            case LibraryMethods.CleanUpLibrary:
            {
                var types = request.FolderPath is not null ? TypesUnder(Folder(library, Segments(request.FolderPath))).ToList()
                    : new List<FakeType> { Type(library, request).Type };
                var unused = types.SelectMany(t => t.Versions.Where(v => v.State == "InWork" && v.Instances.Count == 0).Select(v => (t, v))).ToList();
                consequences.Add($"Clean-up deletes {unused.Count} unused version(s).");
                if (mutate) foreach (var (type, version) in unused) type.Versions.Remove(version);
                return new($"clean up {(request.FolderPath is not null ? "type folder " + LibraryPolicy.FormatPath(Segments(request.FolderPath)) : "type " + Type(library, request).Path)} of {library.Selector}",
                    created, null, null) { Note = mutate ? $"{unused.Count} unused version(s) removed." : null };
            }
            case LibraryMethods.HarmonizeProject:
            {
                if (request.FolderPath is not null) Folder(library, Segments(request.FolderPath)); else Type(library, request);
                Plc(request.PlcName);
                consequences.Add("HarmonizeProject renames or moves the PLC's instances to the library's " + string.Join(" and ", request.HarmonizeOptions ?? new List<string>()) + "; compile afterwards.");
                return new($"harmonize PLC 'PLC_1' from {library.Selector}", created, null, null);
            }
            case LibraryMethods.UpdateLibrary:
            {
                if (request.FolderPath is not null) Folder(library, Segments(request.FolderPath)); else Type(library, request);
                var target = Resolve(request.TargetLibrary);
                consequences.Add(request.ForceUpdateMode is null ? "The two-argument UpdateLibrary applies TIA Portal's default behavior." : "UpdateLibrary with modes.");
                return new($"update {target.Selector} from {library.Selector}", created, null, null);
            }
            case LibraryMethods.DeleteType:
            {
                var (type, path) = Type(library, request);
                consequences.Add($"Deletes the type with its {type.Versions.Count} version(s).");
                if (mutate) Remove(library.Root, type);
                return new($"type {path}", created, null, null);
            }
            case LibraryMethods.SetTypeDoNotUse:
            {
                var (type, path) = Type(library, request);
                if (mutate) type.DoNotUse = request.DoNotUse!.Value;
                return new($"type {path}", created, null, null) { Note = mutate ? $"LibraryType.DoNotUse reads {type.DoNotUse}." : null };
            }
            case LibraryMethods.CreateTypeFolder:
            {
                var segments = Segments(request.FolderPath);
                var parent = Folder(library, segments);
                if (mutate)
                {
                    if (parent.Folders.Any(f => string.Equals(f.Name, request.NewName, StringComparison.OrdinalIgnoreCase)))
                        throw new Refusal(WorkerFailureCategories.EngineeringRejected, $"A folder named '{request.NewName}' already exists.");
                    parent.Folders.Add(new FakeFolder(request.NewName!));
                    created.Add(new LibraryCreatedObjectInfo { Name = request.NewName, RuntimeType = "Fake.LibraryTypeUserFolder" });
                }

                return new($"new type folder {LibraryPolicy.FormatPath(segments.Concat(new[] { request.NewName! }))}", created, mutate ? request.NewName : null, null);
            }
            case LibraryMethods.DeleteTypeFolder:
            {
                var segments = Segments(request.FolderPath);
                var folder = Folder(library, segments);
                consequences.Add($"Deletes the folder with everything in it: {folder.Types.Count} type(s), {folder.Folders.Count} subfolder(s).");
                if (mutate) Folder(library, segments.Take(segments.Count - 1).ToList()).Folders.Remove(folder);
                return new($"type folder {LibraryPolicy.FormatPath(segments)}", created, null, null);
            }
            case LibraryMethods.CreateMasterCopyFolder:
            {
                var segments = Segments(request.MasterCopyFolderPath);
                var parent = MasterCopyFolder(library, segments);
                if (mutate)
                {
                    parent.Folders.Add(new FakeMasterCopyFolder(request.NewName!));
                    created.Add(new LibraryCreatedObjectInfo { Name = request.NewName, RuntimeType = "Fake.MasterCopyUserFolder" });
                }

                return new($"new master copy folder {LibraryPolicy.FormatPath(segments.Concat(new[] { request.NewName! }))}", created, mutate ? request.NewName : null, null);
            }
            case LibraryMethods.DeleteMasterCopyFolder:
            {
                var segments = Segments(request.MasterCopyFolderPath);
                var folder = MasterCopyFolder(library, segments);
                consequences.Add($"Deletes the folder with everything in it: {folder.MasterCopies.Count} master copy(ies), {folder.Folders.Count} subfolder(s).");
                if (mutate) MasterCopyFolder(library, segments.Take(segments.Count - 1).ToList()).Folders.Remove(folder);
                return new($"master copy folder {LibraryPolicy.FormatPath(segments)}", created, null, null);
            }
            case LibraryMethods.RenameMasterCopyFolder:
            {
                var segments = Segments(request.MasterCopyFolderPath);
                var folder = MasterCopyFolder(library, segments);
                if (mutate) folder.Name = request.NewName!;
                return new($"master copy folder {LibraryPolicy.FormatPath(segments)}", created, mutate ? folder.Name : null, null);
            }
            case LibraryMethods.CreateTypeFromDocuments:
            {
                RequireDocuments(request.DocumentDirectory);
                if (request.PlcName is not null) Plc(request.PlcName);
                var folder = Folder(library, Segments(request.FolderPath));
                var transfer = new LibraryTransferInfo { State = "Success", Messages = new List<string> { $"Type '{request.DocumentName}' created." } };
                if (mutate)
                {
                    var type = new FakeType(request.DocumentName!, LibraryPolicy.TypeKinds.CodeBlock);
                    type.Versions.Add(new FakeVersion("0.0.1", "InWork", true));
                    folder.Types.Add(type);
                    created.Add(new LibraryCreatedObjectInfo { Name = type.Name, RuntimeType = "Fake.CodeBlockLibraryType" });
                }

                return new($"new type from documents '{request.DocumentName}'", created, null, null) { Transfer = mutate ? transfer : null };
            }
            case LibraryMethods.CreateVersionFromDocuments:
            {
                RequireDocuments(request.DocumentDirectory);
                var (type, path) = Type(library, request);
                if (request.CreateOption == "Override") consequences.Add("CreateOptions Override overwrites the type's current in-work or in-test version.");
                if (!mutate) return new($"new version of type {path} from documents '{request.DocumentName}'", created, null, null);
                var inWork = type.Versions.FirstOrDefault(v => v.State == "InWork");
                if (inWork is not null && request.CreateOption != "Override")
                    throw new Refusal(WorkerFailureCategories.EngineeringRejected, $"Type '{type.Name}' already has an in-work version; use createOption Override.");
                if (inWork is not null) type.Versions.Remove(inWork);
                var version = new FakeVersion(NextNumber(type.Versions.Last().Number), "InWork", false);
                type.Versions.Add(version);
                return new($"new version of type {path} from documents '{request.DocumentName}'", created, null, Summary(version))
                {
                    Transfer = new LibraryTransferInfo { State = "Success", Messages = new List<string> { $"Version {version.Number} created." } },
                };
            }
            case LibraryMethods.ExportTypeVersion:
            {
                var (_, path, version) = Version(library, request, allowAll: false);
                RequireExport(request.FilePath, request.Overwrite);
                if (mutate) _files.Add(request.FilePath!);
                return new($"type {path} version {version!.Number} to {request.FilePath}", created, null, null)
                {
                    Transfer = mutate ? new LibraryTransferInfo { Files = new List<LibraryFileInfo> { new() { Path = request.FilePath!, Bytes = 4096, Sha256 = new string('c', 64) } } } : null,
                };
            }
            case LibraryMethods.ExportTypeVersionAsDocuments:
            {
                var (_, path, version) = Version(library, request, allowAll: false);
                if (!string.Equals(request.DocumentDirectory, ExportDirectory, StringComparison.OrdinalIgnoreCase))
                    throw new Refusal(WorkerFailureCategories.TargetNotFound, $"documentDirectory '{request.DocumentDirectory}' does not exist.");
                var file = $@"{ExportDirectory}\{request.DocumentName}.s7dcl";
                RequireExport(file, request.Overwrite);
                if (mutate) _files.Add(file);
                return new($"type {path} version {version!.Number} as {request.ExportFormat} documents", created, null, null)
                {
                    Transfer = mutate ? new LibraryTransferInfo
                    {
                        State = "Success", Documents = new List<string> { file },
                        Files = new List<LibraryFileInfo> { new() { Path = file, Bytes = 512, Sha256 = new string('d', 64) } },
                    } : null,
                };
            }
            default:
                return null;
        }
    }

    private static IEnumerable<FakeType> TypesUnder(FakeFolder folder) => folder.Types.Concat(folder.Folders.SelectMany(TypesUnder));

    private static bool Remove(FakeFolder folder, FakeType type)
        => folder.Types.Remove(type) || folder.Folders.Any(child => Remove(child, type));

    private static void RequireDocuments(string? directory)
    {
        if (!string.Equals(directory, DocumentsDirectory, StringComparison.OrdinalIgnoreCase))
            throw new Refusal(WorkerFailureCategories.TargetNotFound, $"documentDirectory '{directory}' does not exist.");
    }

    private void RequireExport(string? file, bool overwrite)
    {
        if (file is null || !file.StartsWith(ExportDirectory + @"\", StringComparison.OrdinalIgnoreCase))
            throw new Refusal(WorkerFailureCategories.TargetNotFound, $"The directory of '{file}' does not exist.");
        if (_files.Contains(file) && !overwrite) throw new Refusal(WorkerFailureCategories.ValidationError, $"'{file}' exists; set overwrite:true to replace it.");
    }
}
