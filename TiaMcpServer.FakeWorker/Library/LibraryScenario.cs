using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.FakeWorker.Library;

/// <summary>
/// Stateful <c>library-roundtrip</c> fixture. Project library: root type <c>MotorData</c> (PLC data type, 1.0.0
/// committed, default), folder <c>/Motors</c> with code-block type <c>PumpControl</c> (1.0.0 committed default with
/// instance <c>PumpControl</c> in PLC_1; 1.0.1 in work) and empty subfolder <c>/Motors/Legacy</c>; root master copies
/// <c>Valve</c> (FB) and <c>Station</c> (Device). One closed user library is available at
/// <c>C:\Libraries\Plant\Plant.al21</c> (type <c>Conveyor</c> 2.0.0, master copy <c>Pump</c>) and as the archive
/// <c>C:\Libraries\Archive\Plant.zal21</c>; directories <c>C:\Libraries\New</c> and <c>C:\Libraries\Retrieved</c> exist.
/// PLC_1 holds blocks Main and PumpControl and the data type MotorData. Reads observe earlier writes in the same
/// process. Scripted refusals: Edit of a type that already has an in-work version and a ThrowIfExists instantiation
/// over an existing block are <c>engineering_rejected</c>; UpdateCheck on the project library is missing
/// (<c>unsupported_capability</c>). TIA Portal confirmations are scripted: closing a library named <c>Prompting</c>
/// (create it with create_global_library) raises one save prompt, answered by the item's portalConfirmations policy, and
/// the close proceeds; release_type_version with comment <c>reject-after-confirmation</c> raises one prompt and then is
/// <c>engineering_rejected</c> with the evidence. Closing a library named <c>LatePrompt</c> models a dialog raised after
/// the item returned: like the worker's lingering scope, the next library response carries the late-dialog warning.
/// release_type_version with comment <c>unreadable-version-number</c> releases but reports its postcondition as
/// <c>unverified</c> (the worker's evidence when a read-back member cannot be read).
/// This models object access only; it is no evidence of Siemens behavior.
/// </summary>
sealed partial class LibraryScenario
{
    private sealed class FakeVersion(string number, string state, bool isDefault)
    {
        public string Number { get; set; } = number;
        public Guid Guid { get; } = Guid.NewGuid();
        public string State { get; set; } = state;
        public bool IsDefault { get; set; } = isDefault;
        public List<string> Instances { get; } = new();
    }

    private sealed class FakeType(string name, string kind)
    {
        public string Name { get; set; } = name;
        public Guid Guid { get; } = Guid.NewGuid();
        public string Kind { get; } = kind;
        public List<FakeVersion> Versions { get; } = new();
        public bool DoNotUse { get; set; }
    }

    /// <summary>A master copy folder (the root folder is FakeLibrary.MasterCopyRoot).</summary>
    private sealed class FakeMasterCopyFolder(string name)
    {
        public string Name { get; set; } = name;
        public List<FakeMasterCopy> MasterCopies { get; } = new();
        public List<FakeMasterCopyFolder> Folders { get; } = new();
    }

    private sealed class FakeFolder(string name)
    {
        public string Name { get; set; } = name;
        public List<FakeType> Types { get; } = new();
        public List<FakeFolder> Folders { get; } = new();
    }

    private sealed class FakeMasterCopy(string name, string contentType)
    {
        public string Name { get; set; } = name;
        public string ContentType { get; } = contentType;
    }

    private sealed class FakeLibrary(string kind, string? path, string? name)
    {
        public string Kind { get; } = kind;
        public string? Path { get; set; } = path;
        public string? Name { get; } = name;
        public FakeFolder Root { get; } = new(string.Empty);
        public FakeMasterCopyFolder MasterCopyRoot { get; } = new(string.Empty);
        public List<FakeMasterCopy> MasterCopies => MasterCopyRoot.MasterCopies;
        public string Selector => Path ?? LibraryPolicy.ProjectLibrary;
        public LibraryRefInfo Ref => new() { Library = Selector, Kind = Kind, Path = Path, Name = Name, RuntimeType = "Fake." + Kind };
    }

    private sealed class Refusal(string category, string message, LibraryWriteResultInfo? evidence = null) : Exception(message)
    {
        public string Category { get; } = category;

        public LibraryWriteResultInfo? Evidence { get; } = evidence;
    }

    /// <summary>The late-dialog warning a lingering scope left for this response (the worker's BeginRequest), at most once.</summary>
    private List<string> TakeLate()
    {
        var warnings = _lateWarning is null ? new List<string>() : new List<string> { _lateWarning };
        _lateWarning = null;
        return warnings;
    }

    /// <summary>The scripted TIA Portal save prompt, answered like the worker's confirmation scope answers it.</summary>
    private static void Confirm(LibraryWriteResultInfo result, string text)
    {
        result.PortalConfirmations.Add(new PlcDownloadPortalConfirmationInfo
        {
            Type = "Siemens.Engineering.ConfirmationEventArgs",
            Text = text,
            Answer = PlcDownloadPolicy.PortalConfirmationPolicies.AnswerFor(result.PortalConfirmationPolicy),
        });
        result.PortalConfirmationsObserved++;
    }

    private const string PlantPath = @"C:\Libraries\Plant\Plant.al21";
    private const string ArchivePath = @"C:\Libraries\Archive\Plant.zal21";

    /// <summary>K80: a system library TIA Portal opens only by its GlobalLibraryInfo (a path is refused).</summary>
    private const string SystemPath = @"C:\Program Files\Siemens\Automation\Portal V21\Data\Lib\PrintingSysLib.as21";

    private static readonly GlobalLibraryDescriptorInfo[] SystemInfos =
        { new() { LibraryType = "System", Name = "PrintingSysLib", Path = SystemPath } };
    private readonly FakeLibrary _project = new(LibraryPolicy.LibraryKinds.Project, null, null);
    private readonly List<FakeLibrary> _open = new();
    private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase) { PlantPath, ArchivePath, SystemPath };
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase) { @"C:\Libraries\New", @"C:\Libraries\Retrieved" };
    private readonly List<string> _blocks = new() { "Main", "PumpControl" };
    private readonly List<string> _devices = new() { "PLC_1" };
    private string? _lateWarning;

    public LibraryScenario()
    {
        var motorData = new FakeType("MotorData", LibraryPolicy.TypeKinds.PlcType);
        motorData.Versions.Add(new FakeVersion("1.0.0", "Committed", true));
        _project.Root.Types.Add(motorData);
        var motors = new FakeFolder("Motors");
        var pump = new FakeType("PumpControl", LibraryPolicy.TypeKinds.CodeBlock);
        var released = new FakeVersion("1.0.0", "Committed", true);
        released.Instances.Add("PumpControl");
        pump.Versions.Add(released);
        pump.Versions.Add(new FakeVersion("1.0.1", "InWork", false));
        motors.Types.Add(pump);
        motors.Folders.Add(new FakeFolder("Legacy"));
        _project.Root.Folders.Add(motors);
        _project.MasterCopies.Add(new FakeMasterCopy("Valve", "Siemens.Engineering.SW.Blocks.FB"));
        _project.MasterCopies.Add(new FakeMasterCopy("Station", "Siemens.Engineering.HW.Device"));
    }

    private static FakeLibrary Plant()
    {
        var plant = new FakeLibrary(LibraryPolicy.LibraryKinds.User, PlantPath, "Plant");
        var conveyor = new FakeType("Conveyor", LibraryPolicy.TypeKinds.CodeBlock);
        conveyor.Versions.Add(new FakeVersion("2.0.0", "Committed", true));
        plant.Root.Types.Add(conveyor);
        plant.MasterCopies.Add(new FakeMasterCopy("Pump", "Siemens.Engineering.SW.Blocks.FB"));
        return plant;
    }

    public string Handle(string line)
    {
        var request = JsonSerializer.Deserialize<WorkerRequest>(line, WorkerJson.Envelope)!;
        // Like the worker's BeginRequest: what a lingering scope answered since the previous request belongs to this response.
        var late = TakeLate();
        try
        {
            var info = request.Library ?? throw new Refusal(WorkerFailureCategories.ValidationError, "missing library request");
            object payload = request.Method == LibraryMethods.Plan ? Plan(info)
                : LibraryMethods.IsReadMethod(request.Method) ? Read(LibraryMethods.OperationOf(request.Method)!, info)
                : LibraryMethods.IsWriteMethod(request.Method) ? Write(LibraryMethods.OperationOf(request.Method)!, info)
                : throw new Refusal(WorkerFailureCategories.ValidationError, $"unexpected library-roundtrip method '{request.Method}'");
            return JsonSerializer.Serialize(new WorkerResponse { Success = true, Payload = WorkerJson.SerializePayload(payload), Warnings = late },
                WorkerJson.Envelope);
        }
        catch (Refusal refusal)
        {
            return JsonSerializer.Serialize(new WorkerResponse
            {
                Success = false, FailureCategory = refusal.Category, Error = refusal.Message, Warnings = late,
                EngineeringError = refusal.Category == WorkerFailureCategories.EngineeringRejected
                    ? new EngineeringErrorInfo { Type = "EngineeringTargetInvocationException", Message = refusal.Message } : null,
                PlcDownloadOutcome = refusal.Evidence is null ? null : WorkerJson.SerializePayload(refusal.Evidence),
            }, WorkerJson.Envelope);
        }
    }

    // ---------------------------------------------------------------- reads

    private object Read(string operation, LibraryRequestInfo request)
    {
        switch (operation)
        {
            case LibraryMethods.ListLibraries:
                return new LibraryListInfo { Libraries = new[] { _project }.Concat(_open).Select(l => l.Ref).ToList() };
            case LibraryMethods.ListGlobalLibraryInfos:
                return new GlobalLibraryDescriptorListInfo
                {
                    Infos = new[] { new GlobalLibraryDescriptorInfo { LibraryType = "User", Name = "Plant", Path = PlantPath, IsOpen = IsOpen(PlantPath) } }
                        .Concat(SystemInfos.Select(info => new GlobalLibraryDescriptorInfo { LibraryType = info.LibraryType, Name = info.Name, Path = info.Path, IsOpen = IsOpen(info.Path) }))
                        .Concat(_open.Where(l => !LibraryPolicy.SamePath(l.Path, PlantPath) && !SystemInfos.Any(info => LibraryPolicy.SamePath(info.Path, l.Path)))
                            .Select(l => new GlobalLibraryDescriptorInfo { LibraryType = "User", Name = l.Name, Path = l.Path, IsOpen = true })).ToList(),
                };
            case LibraryMethods.GetLibraryTree:
                return Tree(Resolve(request.Library), request);
            case LibraryMethods.GetLibraryType:
            {
                var library = Resolve(request.Library);
                var (type, path) = Type(library, request);
                return new LibraryTypeDetailInfo
                {
                    Library = library.Ref, Path = path, Name = type.Name, Guid = type.Guid.ToString(), TypeKind = type.Kind, RuntimeType = "Fake.LibraryType",
                    Versions = type.Versions.Select(v => new LibraryVersionDetailInfo
                    {
                        VersionNumber = v.Number, Guid = v.Guid.ToString(), State = v.State, IsDefault = v.IsDefault,
                        MasterCopiesContainingInstances = new List<string>(), OriginalLibrary = null,
                    }).ToList(),
                    DoNotUse = type.DoNotUse, SetForUpdate = false, Status = "Consistent", Namespace = null, Author = "Fake", Comment = null,
                };
            }
            case LibraryMethods.FindTypeInstances:
            {
                var library = Resolve(request.Library);
                var (type, _, version) = Version(library, request, allowAll: true);
                Plc(request.PlcName);
                return new LibraryTypeInstancesInfo
                {
                    Library = library.Ref, TypeName = type.Name, TypeGuid = type.Guid.ToString(), PlcName = "PLC_1", DeviceName = "PLC_1",
                    Versions = (version is null ? type.Versions : new List<FakeVersion> { version }).Select(v => new LibraryVersionInstancesInfo
                    {
                        VersionNumber = v.Number, Guid = v.Guid.ToString(), Complete = true,
                        Instances = v.Instances.Select(name => new LibraryInstanceInfo { Name = name, RuntimeType = "Fake.FB" }).ToList(),
                    }).ToList(),
                };
            }
            case LibraryMethods.GetInstanceInfo:
            {
                var name = (request.BlockPath ?? request.PlcTypePath)!.Split('/').Last();
                if (!_blocks.Contains(name, StringComparer.OrdinalIgnoreCase) && name != "MotorData")
                    throw new Refusal(WorkerFailureCategories.TargetNotFound, $"Block '{name}' was not found.");
                var link = AllTypes(_project).SelectMany(t => t.Type.Versions.Select(v => (t.Type, Version: v)))
                    .FirstOrDefault(pair => pair.Version.Instances.Contains(name, StringComparer.OrdinalIgnoreCase));
                return new LibraryInstanceLinkInfo
                {
                    PlcName = null, ObjectKind = request.BlockPath is not null ? LibraryPolicy.SourceKinds.Block : LibraryPolicy.SourceKinds.PlcType,
                    ObjectName = name, Linked = link.Type is not null, TypeName = link.Type?.Name, TypeGuid = link.Type?.Guid.ToString(),
                    VersionNumber = link.Version?.Number, VersionGuid = link.Version?.Guid.ToString(), VersionState = link.Version?.State,
                };
            }
            case LibraryMethods.UpdateCheck:
            {
                var library = Resolve(request.Library);
                if (library == _project)
                    throw new Refusal(WorkerFailureCategories.UnsupportedCapability,
                        "This TIA Portal installation exposes no UpdateCheck(Project, UpdateCheckMode) on 'Fake.ProjectLibrary'. (UpdateCheck(Project, UpdateCheckMode)) Nothing was changed.");
                return new LibraryUpdateCheckInfo
                {
                    Library = library.Ref, Mode = request.UpdateCheckMode ?? LibraryPolicy.DefaultUpdateCheckMode, MessageCount = 2, Complete = true,
                    Messages = new List<LibraryMessageInfo>
                    {
                        new() { Description = "Out-of-date instances", MessageParts = new List<string> { "PLC_1" },
                            Messages = new List<LibraryMessageInfo> { new() { Description = "PumpControl uses an older version" } } },
                    },
                };
            }
            case LibraryMethods.CompareLibraries:
            {
                var left = Resolve(request.Library);
                var right = Resolve(request.TargetLibrary);
                var children = right.Root.Types.Select(t => new LibraryCompareElementInfo
                {
                    ComparisonResult = left.Root.Types.Any(l => l.Name == t.Name) ? "ObjectsIdentical" : "LeftMissing",
                    Attributes = new List<LibraryAttributeInfo> { new() { Name = "RightName", Value = t.Name } },
                }).ToList();
                return new LibraryCompareInfo
                {
                    Library = left.Ref, CompareTo = right.Ref, MaxDepth = request.MaxDepth, MaxElements = request.MaxElements, Complete = true,
                    ElementsListed = 1 + children.Count,
                    Root = new LibraryCompareElementInfo
                    {
                        ComparisonResult = "ContainerContentsDifferent", Elements = children,
                        Details = new List<LibraryCompareDetailInfo> { new() { Attributes = new List<LibraryAttributeInfo> { new() { Name = "Name", Value = "Types" } } } },
                    },
                };
            }
            default:
                return ReadExtended(operation, request);
        }
    }

    private static LibraryTreeInfo Tree(FakeLibrary library, LibraryRequestInfo request)
    {
        var start = Segments(request.FolderPath);
        var content = request.Content ?? LibraryPolicy.TreeContents.All;
        var result = new LibraryTreeInfo
        {
            Library = library.Ref, FolderPath = LibraryPolicy.FormatPath(start), Content = content, MaxDepth = request.MaxDepth,
            Offset = request.Offset, Limit = request.Limit, Complete = true,
        };
        var entries = new List<LibraryTreeEntryInfo>();
        if (content != LibraryPolicy.TreeContents.MasterCopies) Walk(Folder(library, start), start.ToList(), 1);
        if (content != LibraryPolicy.TreeContents.Types && (start.Count == 0 || request.MasterCopyFolderPath is not null))
        {
            var masterStart = Segments(request.MasterCopyFolderPath);
            WalkMasterCopies(MasterCopyFolder(library, masterStart), masterStart.ToList(), 1);
        }
        result.Total = entries.Count;
        result.Entries = LibraryPolicy.Page(entries, request.Offset, request.Limit);
        result.Returned = result.Entries.Count;
        return result;

        void Walk(FakeFolder folder, List<string> path, int depth)
        {
            if (depth > request.MaxDepth)
            {
                if (result.Complete) { result.Complete = false; result.Omission = $"maxDepth {request.MaxDepth} cut the walk below {LibraryPolicy.FormatPath(path)}"; }
                return;
            }

            foreach (var type in folder.Types)
                entries.Add(new LibraryTreeEntryInfo
                {
                    Kind = LibraryPolicy.EntryKinds.Type, Path = LibraryPolicy.FormatPath(path.Concat(new[] { type.Name })), Name = type.Name,
                    Depth = depth, Guid = type.Guid.ToString(), TypeKind = type.Kind, RuntimeType = "Fake.LibraryType",
                    Versions = request.IncludeVersions
                        ? type.Versions.Select(v => new LibraryVersionSummaryInfo { VersionNumber = v.Number, Guid = v.Guid.ToString(), State = v.State, IsDefault = v.IsDefault }).ToList()
                        : null,
                });
            foreach (var child in folder.Folders)
            {
                var childPath = path.Concat(new[] { child.Name }).ToList();
                entries.Add(new LibraryTreeEntryInfo { Kind = LibraryPolicy.EntryKinds.TypeFolder, Path = LibraryPolicy.FormatPath(childPath), Name = child.Name, Depth = depth, RuntimeType = "Fake.LibraryTypeUserFolder" });
                Walk(child, childPath, depth + 1);
            }
        }

        void WalkMasterCopies(FakeMasterCopyFolder folder, List<string> path, int depth)
        {
            entries.AddRange(folder.MasterCopies.Select(mc => new LibraryTreeEntryInfo
            {
                Kind = LibraryPolicy.EntryKinds.MasterCopy, Path = LibraryPolicy.FormatPath(path.Concat(new[] { mc.Name })), Name = mc.Name, Depth = depth,
                RuntimeType = "Fake.MasterCopy", ContentTypes = new List<string> { mc.ContentType },
            }));
            foreach (var child in folder.Folders)
            {
                var childPath = path.Concat(new[] { child.Name }).ToList();
                entries.Add(new LibraryTreeEntryInfo { Kind = LibraryPolicy.EntryKinds.MasterCopyFolder, Path = LibraryPolicy.FormatPath(childPath), Name = child.Name, Depth = depth, RuntimeType = "Fake.MasterCopyUserFolder" });
                WalkMasterCopies(child, childPath, depth + 1);
            }
        }
    }

    // ---------------------------------------------------------------- planning

    private LibraryPlanInfo Plan(LibraryRequestInfo request)
    {
        if (request.Operation == LibraryMethods.SetPortalSetting) return (LibraryPlanInfo)PortalSettingItem(request, mutate: false, null);
        var plan = new LibraryPlanInfo { Operation = request.Operation };
        if (LibraryMethods.IsSecondaryProject(request.Operation))
        {
            SecondaryProjectItem(request, plan, null);
            return plan;
        }

        switch (request.Operation)
        {
            case LibraryMethods.OpenGlobalLibrary:
                RequireFile(request.Path);
                plan.AlreadyOpen = IsOpen(request.Path);
                if (!plan.AlreadyOpen && SystemInfo(request) is { } planned)
                {
                    plan.Target = $"{planned.LibraryType} global library {request.Path} ({GlobalLibraryInfoOpen.Call}, read-only)";
                    plan.Consequences.Add(GlobalLibraryInfoOpen.Consequence(planned.LibraryType!));
                    break;
                }

                plan.Target = $"global library file {request.Path} ({(request.Upgrade ? "OpenWithUpgrade" : "OpenMode " + (request.OpenMode ?? LibraryPolicy.DefaultOpenMode))})";
                if (plan.AlreadyOpen) plan.Consequences.Add("The file is already open as a global library; nothing is called and its open mode is not changed.");
                break;
            case LibraryMethods.RetrieveGlobalLibrary:
                RequireFile(request.Path);
                plan.Target = $"archive {request.Path} into {request.TargetDirectory}";
                plan.Consequences.Add($"Retrieve extracts the archive into {request.TargetDirectory} and opens the extracted library.");
                break;
            case LibraryMethods.CreateGlobalLibrary:
                RequireDirectory(request.TargetDirectory);
                plan.Target = $"new user global library '{request.Name}' in {request.TargetDirectory}";
                plan.Consequences.Add($"Creates library files in {request.TargetDirectory}.");
                break;
            case LibraryMethods.ArchiveGlobalLibrary:
            case LibraryMethods.SaveGlobalLibraryAs:
            {
                var library = Resolve(request.Library);
                RequireDirectory(request.TargetDirectory);
                plan.Library = library.Ref;
                plan.Target = request.Operation == LibraryMethods.ArchiveGlobalLibrary
                    ? $"archive {library.Path} as '{request.Name}' in {request.TargetDirectory} (LibraryArchivationMode {request.ArchivationMode})"
                    : $"save {library.Path} as a copy in {request.TargetDirectory}";
                plan.Consequences.Add($"Writes into {request.TargetDirectory}.");
                break;
            }
            case LibraryMethods.SaveGlobalLibrary:
            case LibraryMethods.CloseGlobalLibrary:
            {
                var library = Resolve(request.Library);
                plan.Library = library.Ref;
                plan.Target = (request.Operation == LibraryMethods.SaveGlobalLibrary ? "save " : "close ") + library.Path;
                plan.Consequences.Add(request.Operation == LibraryMethods.SaveGlobalLibrary ? $"Overwrites the library file {library.Path}."
                    : "Close() takes no save-or-discard argument and no documented member reports unsaved changes: save first with "
                        + "save_global_library; a TIA Portal confirmation raised by Close() is answered by portalConfirmations and recorded.");
                break;
            }
            default:
            {
                var library = Resolve(request.Library);
                plan.Library = library.Ref;
                plan.Target = Content(library, request, plan.Consequences, mutate: false).Target;
                break;
            }
        }

        return plan;
    }

    // ---------------------------------------------------------------- writes

    private LibraryWriteResultInfo Write(string operation, LibraryRequestInfo request)
    {
        if (!PlcDownloadPolicy.PortalConfirmationPolicies.IsKnown(request.PortalConfirmations))
            throw new Refusal(WorkerFailureCategories.ValidationError, "portalConfirmations must be decline or accept.");
        var result = new LibraryWriteResultInfo { Operation = operation, PortalConfirmationPolicy = request.PortalConfirmations! };
        if (LibraryMethods.IsSecondaryProject(operation)) return SecondaryProjectItem(request, null, result)!;
        if (operation == LibraryMethods.SetPortalSetting) return (LibraryWriteResultInfo)PortalSettingItem(request, mutate: true, result);
        switch (operation)
        {
            case LibraryMethods.OpenGlobalLibrary:
                RequireFile(request.Path);
                result.Target = $"global library file {request.Path}";
                if (IsOpen(request.Path))
                {
                    result.AlreadyOpen = true;
                    result.Library = _open.First(l => LibraryPolicy.SamePath(l.Path, request.Path)).Ref;
                    result.Note = "The file was already open; Open was not called and the library's open mode was not changed.";
                    return result;
                }

                if (SystemInfo(request) is { } info)
                {
                    var system = new FakeLibrary(LibraryPolicy.LibraryKinds.System, info.Path, info.Name);
                    _open.Add(system);
                    result.Library = system.Ref;
                    result.ReturnedType = "Fake.SystemGlobalLibrary";
                    result.Note = $"{info.LibraryType} global library opened with {GlobalLibraryInfoOpen.Documented} (no OpenMode; TIA Portal opens it read-only).";
                    return result;
                }

                var opened = Plant();
                _open.Add(opened);
                result.Library = opened.Ref;
                result.ReturnedType = "Fake.UserGlobalLibrary";
                return result;
            case LibraryMethods.ArchiveGlobalLibrary:
            {
                var library = Resolve(request.Library);
                RequireDirectory(request.TargetDirectory);
                var archive = $@"{request.TargetDirectory!.TrimEnd('\\')}\{request.Name}.zal21";
                _files.Add(archive);
                result.Library = library.Ref;
                result.Target = $"archive {library.Path} as '{request.Name}' in {request.TargetDirectory}";
                result.Transfer = new LibraryTransferInfo { Files = new List<LibraryFileInfo> { new() { Path = archive, Bytes = 1024, Sha256 = new string('a', 64) } } };
                return result;
            }
            case LibraryMethods.SaveGlobalLibraryAs:
            {
                var library = Resolve(request.Library);
                RequireDirectory(request.TargetDirectory);
                result.Target = $"save {library.Path} as a copy in {request.TargetDirectory}";
                library.Path = $@"{request.TargetDirectory!.TrimEnd('\\')}\{library.Name}\{library.Name}.al21";
                result.Library = library.Ref;
                result.Note = $"SaveAs returned; the open library now refers to {library.Path} (use that path as its library selector).";
                result.Transfer = new LibraryTransferInfo { Files = new List<LibraryFileInfo> { new() { Path = library.Path, Bytes = 2048, Sha256 = new string('b', 64) } } };
                return result;
            }
            case LibraryMethods.RetrieveGlobalLibrary:
            {
                RequireFile(request.Path);
                var retrieved = Plant();
                retrieved.Path = request.TargetDirectory!.TrimEnd('\\') + @"\Plant\Plant.al21";
                _open.Add(retrieved);
                result.Target = $"archive {request.Path}";
                result.Library = retrieved.Ref;
                result.ReturnedType = "Fake.UserGlobalLibrary";
                return result;
            }
            case LibraryMethods.CreateGlobalLibrary:
            {
                RequireDirectory(request.TargetDirectory);
                var created = new FakeLibrary(LibraryPolicy.LibraryKinds.User, $@"{request.TargetDirectory!.TrimEnd('\\')}\{request.Name}\{request.Name}.al21", request.Name);
                _open.Add(created);
                result.Target = $"new library '{request.Name}' in {request.TargetDirectory}";
                result.Library = created.Ref;
                result.ReturnedType = "Fake.UserGlobalLibrary";
                return result;
            }
            case LibraryMethods.SaveGlobalLibrary:
            {
                var library = Resolve(request.Library);
                result.Library = library.Ref;
                result.Target = "save " + library.Path;
                return result;
            }
            case LibraryMethods.CloseGlobalLibrary:
            {
                var library = Resolve(request.Library);
                if (library.Name == "Prompting") Confirm(result, $"Do you want to save the changes to the library '{library.Name}'?");
                if (library.Name == "LatePrompt")
                    _lateWarning = $"1 TIA Portal confirmation dialog(s) were raised after library_close_global_library returned and were answered "
                        + $"{PlcDownloadPolicy.PortalConfirmationPolicies.AnswerFor(result.PortalConfirmationPolicy)} by its portalConfirmations policy "
                        + "(Siemens.Engineering.ConfirmationEventArgs); they are not in that call's evidence. Inspect the PLC and the project.";
                _open.Remove(library);
                result.Library = library.Ref;
                result.Target = "close " + library.Path;
                return result;
            }
            default:
            {
                var library = Resolve(request.Library);
                result.Library = library.Ref;
                if (operation == LibraryMethods.ReleaseTypeVersion && request.Comment == "reject-after-confirmation")
                {
                    Confirm(result, "Release the version and its dependencies?");
                    throw new Refusal(WorkerFailureCategories.EngineeringRejected, "The release was rejected after the confirmation.", result);
                }

                var outcome = Content(library, request, new List<string>(), mutate: true);
                result.Target = outcome.Target;
                result.Created = outcome.Created;
                result.NameAfter = outcome.NameAfter;
                result.VersionAfter = outcome.VersionAfter;
                result.Note = outcome.Note;
                result.Transfer = outcome.Transfer;
                if (operation == LibraryMethods.ReleaseTypeVersion && request.Comment == "unreadable-version-number")
                    result.Unverified = "Release(...) returned, but LibraryTypeVersion.VersionNumber (a runtime lookup) could not be read, so the released number is not verified.";
                return result;
            }
        }
    }

    private sealed record ContentOutcome(string Target, List<LibraryCreatedObjectInfo> Created, string? NameAfter, LibraryVersionSummaryInfo? VersionAfter)
    {
        public string? Note { get; init; }

        public LibraryTransferInfo? Transfer { get; init; }
    }

    /// <summary>Content items: resolution (plan and write share it) and, with <paramref name="mutate"/>, the change.</summary>
    private ContentOutcome Content(FakeLibrary library, LibraryRequestInfo request, List<string> consequences, bool mutate)
    {
        var created = new List<LibraryCreatedObjectInfo>();
        switch (request.Operation)
        {
            case LibraryMethods.CreateMasterCopy:
            {
                var name = request.Source!.Path.Split('/').Last();
                if (request.Source.Project is not null) RequireSecondarySource(request.Source);
                else if (request.Source.Kind == LibraryPolicy.SourceKinds.Block && !_blocks.Contains(name, StringComparer.OrdinalIgnoreCase))
                    throw new Refusal(WorkerFailureCategories.TargetNotFound, $"Block '{request.Source.Path}' was not found.");
                if (mutate)
                {
                    MasterCopyFolder(library, Segments(request.MasterCopyFolderPath)).MasterCopies.Add(new FakeMasterCopy(name, "Siemens.Engineering.SW.Blocks.FB"));
                    created.Add(new LibraryCreatedObjectInfo { Name = name, RuntimeType = "Fake.MasterCopy" });
                }

                return new($"master copy of {request.Source.Kind} '{request.Source.Path}'", created, null, null);
            }
            case LibraryMethods.CopyMasterCopy:
            {
                var masterCopy = MasterCopy(library, request.MasterCopyName, request.MasterCopyFolderPath);
                var target = Resolve(request.TargetLibrary);
                if (mutate)
                {
                    MasterCopyFolder(target, Segments(request.TargetMasterCopyFolderPath)).MasterCopies.Add(new FakeMasterCopy(masterCopy.Name, masterCopy.ContentType));
                    created.Add(new LibraryCreatedObjectInfo { Name = masterCopy.Name, RuntimeType = "Fake.MasterCopy" });
                }

                return new($"copy of master copy '{masterCopy.Name}' in {target.Selector}", created, null, null);
            }
            case LibraryMethods.DeleteMasterCopy:
            {
                var masterCopy = MasterCopy(library, request.MasterCopyName, request.MasterCopyFolderPath);
                consequences.Add($"Deletes the master copy (content: {LibraryPolicy.ShortTypeName(masterCopy.ContentType)}).");
                if (mutate) MasterCopyFolder(library, Segments(request.MasterCopyFolderPath)).MasterCopies.Remove(masterCopy);
                return new($"master copy '{masterCopy.Name}'", created, null, null);
            }
            case LibraryMethods.RenameMasterCopy:
            {
                var masterCopy = MasterCopy(library, request.MasterCopyName, request.MasterCopyFolderPath);
                var target = mutate ? $"master copy '{masterCopy.Name}'" : $"master copy '{masterCopy.Name}' -> '{request.NewName}'";
                if (mutate) masterCopy.Name = request.NewName!;
                return new(target, created, mutate ? masterCopy.Name : null, null);
            }
            case LibraryMethods.InstantiateMasterCopy:
            {
                var masterCopy = MasterCopy(library, request.MasterCopyName, request.MasterCopyFolderPath);
                var mode = request.MasterCopyMode ?? LibraryPolicy.DefaultMasterCopyMode;
                var description = Target(request.Target!);
                if (mode == "Replace") consequences.Add($"MasterCopyMode Replace replaces existing objects of the same names in {description}.");
                if (mutate)
                {
                    var names = request.Target!.Kind == LibraryPolicy.TargetKinds.Devices ? _devices : _blocks;
                    if (mode == "ThrowIfExists" && names.Contains(masterCopy.Name, StringComparer.OrdinalIgnoreCase))
                        throw new Refusal(WorkerFailureCategories.EngineeringRejected, $"An object named '{masterCopy.Name}' already exists.");
                    var name = request.NewName ?? masterCopy.Name;
                    if (!names.Contains(name, StringComparer.OrdinalIgnoreCase)) names.Add(name);
                    created.Add(new LibraryCreatedObjectInfo { Name = name, RuntimeType = "Fake." + LibraryPolicy.ShortTypeName(masterCopy.ContentType) });
                }

                return new($"master copy '{masterCopy.Name}' in {description}", created, mutate ? request.NewName : null, null);
            }
            case LibraryMethods.InstantiateTypeVersion:
            {
                var (type, path, version) = Version(library, request, allowAll: false);
                var expected = type.Kind == LibraryPolicy.TypeKinds.CodeBlock ? LibraryPolicy.TargetKinds.BlockGroup : LibraryPolicy.TargetKinds.TypeGroup;
                if (request.Target!.Kind != expected)
                    throw new Refusal(WorkerFailureCategories.TargetKindUnsupported, $"A {type.Kind} type version cannot be instantiated into a {request.Target.Kind}.");
                Plc(request.Target.PlcName);
                if (library != _project) consequences.Add("A global-library type version is synchronized into the project library before the instance is created.");
                if (mutate)
                {
                    version!.Instances.Add(type.Name);
                    if (!_blocks.Contains(type.Name)) _blocks.Add(type.Name);
                    created.Add(new LibraryCreatedObjectInfo { Name = type.Name, RuntimeType = "Fake.FB" });
                }

                return new($"type {path} version {version!.Number} in {Target(request.Target)}", created, null, null);
            }
            case LibraryMethods.UpdateProject:
            {
                var description = request.FolderPath is not null ? "type folder " + LibraryPolicy.FormatPath(Segments(request.FolderPath)) : "type " + Type(library, request).Path;
                if (request.FolderPath is not null) Folder(library, Segments(request.FolderPath));
                Plc(request.PlcName);
                consequences.Add("The documented two-argument UpdateProject applies TIA Portal's default force-update, unused-version and structure-conflict behavior; the mode overloads are not documented.");
                return new($"PLC 'PLC_1' from {description} of {library.Selector}", created, null, null);
            }
            case LibraryMethods.RenameType:
            {
                var (type, path) = Type(library, request);
                if (mutate) type.Name = request.NewName!;
                return new($"type {path}", created, mutate ? type.Name : null, null);
            }
            case LibraryMethods.RenameTypeFolder:
            {
                var segments = Segments(request.FolderPath);
                var folder = Folder(library, segments);
                if (mutate) folder.Name = request.NewName!;
                return new($"type folder {LibraryPolicy.FormatPath(segments)}", created, mutate ? folder.Name : null, null);
            }
            case var extended when ContentExtended(library, request, consequences, mutate) is { } outcome:
                return outcome;
            default:
            {
                var (type, path, version) = Version(library, request, allowAll: false);
                var target = $"type {path} version {version!.Number}";
                if (!mutate)
                {
                    consequences.Add(request.Operation switch
                    {
                        LibraryMethods.DeleteTypeVersion => "No dependent version was read.",
                        LibraryMethods.DiscardTypeVersion => "Discard() discards the in-work version's changes.",
                        _ => "planned",
                    });
                    return new(target, created, null, null);
                }

                switch (request.Operation)
                {
                    case LibraryMethods.EditTypeVersion:
                        if (type.Versions.Any(v => v.State == "InWork"))
                            throw new Refusal(WorkerFailureCategories.EngineeringRejected, $"Type '{type.Name}' already has an in-work version.");
                        var inWork = new FakeVersion(NextNumber(version.Number), "InWork", false);
                        type.Versions.Add(inWork);
                        created.Add(new LibraryCreatedObjectInfo { Name = inWork.Number, RuntimeType = "Fake.LibraryTypeVersion" });
                        return new(target, created, null, Summary(inWork));
                    case LibraryMethods.ReleaseTypeVersion:
                        if (version.State != "InWork") throw new Refusal(WorkerFailureCategories.EngineeringRejected, "Only an in-work version can be released.");
                        version.State = "Committed";
                        version.Number = request.VersionNumber!;
                        return new(target, created, null, Summary(version));
                    case LibraryMethods.DiscardTypeVersion:
                        type.Versions.Remove(version);
                        return new(target, created, null, null);
                    case LibraryMethods.SetDefaultTypeVersion:
                        type.Versions.ForEach(v => v.IsDefault = v == version);
                        return new(target, created, null, Summary(version));
                    case LibraryMethods.DeleteTypeVersion:
                        type.Versions.Remove(version);
                        return new(target, created, null, null);
                    default:
                        throw new Refusal(WorkerFailureCategories.ValidationError, $"unexpected library write '{request.Operation}'");
                }
            }
        }
    }

    // ---------------------------------------------------------------- resolution

    private FakeLibrary Resolve(string? selector)
        => LibraryPolicy.IsProject(selector) ? _project
            : _open.FirstOrDefault(l => LibraryPolicy.SamePath(l.Path, selector))
                ?? throw new Refusal(WorkerFailureCategories.TargetNotFound, $"No open global library has the path '{selector}'.");

    private bool IsOpen(string? path) => _open.Any(l => LibraryPolicy.SamePath(l.Path, path));

    /// <summary>K80: the system library info the path names, through the worker's shared decision (options refused like the worker).</summary>
    private static GlobalLibraryDescriptorInfo? SystemInfo(LibraryRequestInfo request)
    {
        var found = GlobalLibraryInfoOpen.Find(SystemInfos, request.Path!, info => info.Path, info => info.LibraryType, out var ambiguity);
        if (ambiguity is not null) throw new Refusal(WorkerFailureCategories.TargetAmbiguous, ambiguity);
        if (found is not { } match) return null;
        if (GlobalLibraryInfoOpen.OptionRefusal(request, match.LibraryType) is { } refusal) throw new Refusal(WorkerFailureCategories.ValidationError, refusal);
        return match.Info;
    }

    private static IReadOnlyList<string> Segments(string? folderPath)
        => LibraryPolicy.ParseFolderPath(folderPath, out var error) ?? throw new Refusal(WorkerFailureCategories.ValidationError, error!);

    private static FakeFolder Folder(FakeLibrary library, IReadOnlyList<string> segments)
    {
        var current = library.Root;
        foreach (var segment in segments)
            current = current.Folders.FirstOrDefault(f => string.Equals(f.Name, segment, StringComparison.OrdinalIgnoreCase))
                ?? throw new Refusal(WorkerFailureCategories.TargetNotFound, $"Type folder '{LibraryPolicy.FormatPath(segments)}' was not found.");
        return current;
    }

    private static IEnumerable<(FakeType Type, string Path)> AllTypes(FakeLibrary library)
    {
        IEnumerable<(FakeType, string)> Walk(FakeFolder folder, List<string> path)
            => folder.Types.Select(t => (t, LibraryPolicy.FormatPath(path.Concat(new[] { t.Name }))))
                .Concat(folder.Folders.SelectMany(f => Walk(f, path.Concat(new[] { f.Name }).ToList())));
        return Walk(library.Root, new List<string>());
    }

    private static (FakeType Type, string Path) Type(FakeLibrary library, LibraryRequestInfo request)
    {
        if (request.TypeGuid is not null)
            return AllTypes(library).Where(t => t.Type.Guid.ToString() == request.TypeGuid).Cast<(FakeType, string)?>().FirstOrDefault()
                ?? throw new Refusal(WorkerFailureCategories.TargetNotFound, $"No library type has the GUID {request.TypeGuid} (ILibrary.FindType).");
        var parsed = LibraryPolicy.ParseTypePath(request.TypePath, out var error) ?? throw new Refusal(WorkerFailureCategories.ValidationError, error!);
        var folder = Folder(library, parsed.Folder);
        var type = folder.Types.FirstOrDefault(t => string.Equals(t.Name, parsed.Name, StringComparison.OrdinalIgnoreCase))
            ?? throw new Refusal(WorkerFailureCategories.TargetNotFound, $"Library type '{request.TypePath}' was not found.");
        return (type, LibraryPolicy.FormatPath(parsed.Folder.Concat(new[] { type.Name })));
    }

    private static (FakeType Type, string Path, FakeVersion? Version) Version(FakeLibrary library, LibraryRequestInfo request, bool allowAll)
    {
        if (request.VersionGuid is not null)
        {
            foreach (var (candidate, path) in AllTypes(library))
                if (candidate.Versions.FirstOrDefault(v => v.Guid.ToString() == request.VersionGuid) is { } byGuid)
                    return (candidate, path, byGuid);
            throw new Refusal(WorkerFailureCategories.TargetNotFound, $"No library type version has the GUID {request.VersionGuid} (ILibrary.FindVersion).");
        }

        var (type, typePath) = Type(library, request);
        if (request.Version is null)
            return allowAll ? (type, typePath, null)
                : type.Versions.Count == 1 ? (type, typePath, type.Versions[0])
                : throw new Refusal(WorkerFailureCategories.TargetAmbiguous, $"Type '{typePath}' has {type.Versions.Count} versions; name one with version or versionGuid.");
        return (type, typePath, type.Versions.FirstOrDefault(v => v.Number == request.Version)
            ?? throw new Refusal(WorkerFailureCategories.TargetNotFound, $"Type '{typePath}' has no version '{request.Version}'."));
    }

    private static FakeMasterCopy MasterCopy(FakeLibrary library, string? name, string? folderPath = null)
        => MasterCopyFolder(library, Segments(folderPath)).MasterCopies.FirstOrDefault(mc => mc.Name == name)
            ?? throw new Refusal(WorkerFailureCategories.TargetNotFound, $"No master copy named '{name}' is in the master copy folder {LibraryPolicy.FormatPath(Segments(folderPath))}.");

    private static FakeMasterCopyFolder MasterCopyFolder(FakeLibrary library, IReadOnlyList<string> segments)
    {
        var current = library.MasterCopyRoot;
        foreach (var segment in segments)
            current = current.Folders.FirstOrDefault(f => string.Equals(f.Name, segment, StringComparison.OrdinalIgnoreCase))
                ?? throw new Refusal(WorkerFailureCategories.TargetNotFound, $"Master copy folder '{LibraryPolicy.FormatPath(segments)}' was not found.");
        return current;
    }

    private static void Plc(string? plcName)
    {
        if (plcName is not null && plcName != "PLC_1") throw new Refusal(WorkerFailureCategories.TargetNotFound, $"PLC '{plcName}' was not found.");
    }

    private static string Target(LibraryTargetInfo target)
    {
        Plc(target.PlcName);
        return target.Kind switch
        {
            LibraryPolicy.TargetKinds.Devices => "the project's devices",
            LibraryPolicy.TargetKinds.DeviceItem => $"device item of '{target.Parent?.DeviceName}'",
            _ => $"{target.Kind} {LibraryPolicy.FormatPath(Segments(target.FolderPath))} of PLC 'PLC_1'",
        };
    }

    private void RequireFile(string? path)
    {
        if (path is null || !_files.Contains(path)) throw new Refusal(WorkerFailureCategories.TargetNotFound, $"path '{path}' does not exist.");
    }

    private void RequireDirectory(string? path)
    {
        if (path is null || !_directories.Contains(path)) throw new Refusal(WorkerFailureCategories.TargetNotFound, $"targetDirectory '{path}' does not exist.");
    }

    private static LibraryVersionSummaryInfo Summary(FakeVersion version)
        => new() { VersionNumber = version.Number, Guid = version.Guid.ToString(), State = version.State, IsDefault = version.IsDefault };

    private static string NextNumber(string number)
    {
        var parsed = System.Version.Parse(number);
        return new System.Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build) + 1).ToString();
    }
}
