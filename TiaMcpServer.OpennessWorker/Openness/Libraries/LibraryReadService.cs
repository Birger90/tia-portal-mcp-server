using Siemens.Engineering;
using Siemens.Engineering.Compare;
using Siemens.Engineering.Library;
using Siemens.Engineering.Library.MasterCopies;
using Siemens.Engineering.Library.Types;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Types;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Libraries;

/// <summary>The eight <c>library_read</c> operations; none changes a library or the project.</summary>
internal static partial class LibraryReadService
{
    public static object Read(TiaPortal portal, ProjectBase project, string method, LibraryRequestInfo request)
        => LibraryMethods.OperationOf(method) switch
        {
            LibraryMethods.ListLibraries => ListLibraries(portal, project),
            LibraryMethods.ListGlobalLibraryInfos => ListInfos(portal),
            LibraryMethods.GetLibraryTree => Tree(LibraryResolver.Resolve(portal, project, request.Library), request),
            LibraryMethods.GetLibraryType => TypeDetail(LibraryResolver.Resolve(portal, project, request.Library), request),
            LibraryMethods.FindTypeInstances => Instances(LibraryResolver.Resolve(portal, project, request.Library), project, request),
            LibraryMethods.GetInstanceInfo => InstanceLink(project, request),
            LibraryMethods.UpdateCheck => UpdateCheck(LibraryResolver.Resolve(portal, project, request.Library), project, request),
            LibraryMethods.CompareLibraries => Compare(LibraryResolver.Resolve(portal, project, request.Library),
                LibraryResolver.Resolve(portal, project, request.TargetLibrary), request),
            LibraryMethods.ListSecondaryProjects => SecondaryProjects.Read(portal),
            LibraryMethods.ListPortalSettings => PortalSettings.Read(portal, request),
            LibraryMethods.GetSupportedExportFormats => ExportFormats(LibraryResolver.Resolve(portal, project, request.Library), request),
            LibraryMethods.CompareTypes or LibraryMethods.CompareTypeVersions or LibraryMethods.CompareMasterCopies
                => CompareObjects(portal, project, request),
            _ => throw LibraryResolver.Validation($"Unsupported library read method '{method}'."),
        };

    private static LibraryListInfo ListLibraries(TiaPortal portal, ProjectBase project)
    {
        var result = new LibraryListInfo();
        if ((project as Siemens.Engineering.Project)?.ProjectLibrary is { } projectLibrary) result.Libraries.Add(LibraryResolver.ProjectRef(projectLibrary));
        result.Libraries.AddRange(LibraryResolver.OpenGlobals(portal, result.Unreadable).Select(g => g.Ref));
        return result;
    }

    /// <summary><c>GlobalLibraryComposition.GetGlobalLibraryInfos()</c> (return collection undocumented → runtime lookup).</summary>
    private static GlobalLibraryDescriptorListInfo ListInfos(TiaPortal portal)
    {
        var result = new GlobalLibraryDescriptorListInfo();
        var open = LibraryResolver.OpenGlobals(portal, result.Unreadable).Select(g => g.Ref.Path).ToArray();
        var infos = LibraryReflection.Invoke(portal.GlobalLibraries, "GetGlobalLibraryInfos", Type.EmptyTypes, Array.Empty<object>(),
            "GlobalLibraryComposition.GetGlobalLibraryInfos()");
        foreach (var info in LibraryReflection.Enumerate(infos, "global library infos"))
        {
            var path = LibraryReflection.Display(LibraryReflection.Optional(info, "Path", result.Unreadable, "GlobalLibraryInfo.Path"));
            result.Infos.Add(new GlobalLibraryDescriptorInfo
            {
                LibraryType = LibraryReflection.Display(LibraryReflection.Optional(info, "LibraryType", result.Unreadable, "GlobalLibraryInfo.LibraryType")),
                Name = LibraryReflection.Display(LibraryReflection.Optional(info, "Name", result.Unreadable, "GlobalLibraryInfo.Name")),
                Path = path,
                IsOpen = open.Any(candidate => LibraryPolicy.SamePath(candidate, path)),
            });
        }

        result.Unreadable = result.Unreadable.Distinct(StringComparer.Ordinal).ToList();
        return result;
    }

    private static LibraryTreeInfo Tree(ResolvedLibrary library, LibraryRequestInfo request)
    {
        var content = request.Content ?? LibraryPolicy.TreeContents.All;
        var start = LibraryResolver.Segments(request.FolderPath);
        var result = new LibraryTreeInfo
        {
            Library = library.Ref, FolderPath = LibraryPolicy.FormatPath(start), Content = content,
            MaxDepth = request.MaxDepth, Offset = request.Offset, Limit = request.Limit, Complete = true,
        };
        var entries = new List<LibraryTreeEntryInfo>();
        bool Full() => entries.Count >= LibraryPolicy.MaxTreeEntries;

        if (content != LibraryPolicy.TreeContents.MasterCopies)
        {
            var root = LibraryResolver.ResolveFolder(library.Library, start);
            Walk(root, start.ToList(), 1);
        }

        void Walk(LibraryTypeFolder folder, List<string> path, int depth)
        {
            if (Full()) { Incomplete($"the walk stopped at {LibraryPolicy.MaxTreeEntries} entries"); return; }
            if (depth > request.MaxDepth) { Incomplete($"maxDepth {request.MaxDepth} cut the walk below {LibraryPolicy.FormatPath(path)}"); return; }
            foreach (var type in LibraryResolver.Types(folder))
            {
                if (Full()) { Incomplete($"the walk stopped at {LibraryPolicy.MaxTreeEntries} entries"); return; }
                entries.Add(TypeEntry(type, path, depth, request.IncludeVersions));
            }

            foreach (var child in LibraryResolver.ChildFolders(folder))
            {
                if (Full()) { Incomplete($"the walk stopped at {LibraryPolicy.MaxTreeEntries} entries"); return; }
                var unreadable = new List<string>();
                var name = LibraryResolver.FolderName(child, unreadable);
                var childPath = path.Concat(new[] { name ?? "?" }).ToList();
                entries.Add(new LibraryTreeEntryInfo
                {
                    Kind = LibraryPolicy.EntryKinds.TypeFolder, Path = LibraryPolicy.FormatPath(childPath), Name = name, Depth = depth,
                    RuntimeType = LibraryReflection.RuntimeType(child), Unreadable = unreadable,
                });
                if (name is not null) Walk(child, childPath, depth + 1);
            }
        }

        // Master copies: the walk starts at masterCopyFolderPath (default the root master copy folder) and descends through the
        // user folders (MasterCopyFolder.Folders, V21). A type folderPath below the root restricts the tree to types, as before.
        if (content != LibraryPolicy.TreeContents.Types && (start.Count == 0 || request.MasterCopyFolderPath is not null))
        {
            var masterStart = LibraryResolver.Segments(request.MasterCopyFolderPath);
            WalkMasterCopies(LibraryResolver.ResolveMasterCopyFolder(library.Library, masterStart), masterStart.ToList(), 1);
        }

        void WalkMasterCopies(MasterCopyFolder folder, List<string> path, int depth)
        {
            if (Full()) { Incomplete($"the walk stopped at {LibraryPolicy.MaxTreeEntries} entries"); return; }
            if (depth > request.MaxDepth) { Incomplete($"maxDepth {request.MaxDepth} cut the master copy walk below {LibraryPolicy.FormatPath(path)}"); return; }
            foreach (var masterCopy in folder.MasterCopies)
            {
                if (Full()) { Incomplete($"the walk stopped at {LibraryPolicy.MaxTreeEntries} entries"); return; }
                entries.Add(MasterCopyEntry(masterCopy, path, depth));
            }

            foreach (var child in LibraryResolver.MasterCopyChildFolders(folder))
            {
                if (Full()) { Incomplete($"the walk stopped at {LibraryPolicy.MaxTreeEntries} entries"); return; }
                var unreadable = new List<string>();
                var name = LibraryResolver.MasterCopyFolderName(child, unreadable);
                var childPath = path.Concat(new[] { name ?? "?" }).ToList();
                entries.Add(new LibraryTreeEntryInfo
                {
                    Kind = LibraryPolicy.EntryKinds.MasterCopyFolder, Path = LibraryPolicy.FormatPath(childPath), Name = name, Depth = depth,
                    RuntimeType = LibraryReflection.RuntimeType(child), Unreadable = unreadable,
                });
                if (name is not null) WalkMasterCopies(child, childPath, depth + 1);
            }
        }

        result.Total = entries.Count;
        result.Entries = LibraryPolicy.Page(entries, request.Offset, request.Limit);
        result.Returned = result.Entries.Count;
        return result;

        void Incomplete(string reason)
        {
            if (!result.Complete) return;
            result.Complete = false;
            result.Omission = reason;
        }
    }

    private static LibraryTreeEntryInfo TypeEntry(LibraryType type, List<string> folder, int depth, bool includeVersions)
    {
        var unreadable = new List<string>();
        var name = LibraryResolver.SafeName(type);
        if (name is null) unreadable.Add("LibraryType.Name");
        return new LibraryTreeEntryInfo
        {
            Kind = LibraryPolicy.EntryKinds.Type,
            Path = LibraryPolicy.FormatPath(folder.Concat(new[] { name ?? "?" })),
            Name = name,
            Depth = depth,
            Guid = type.Guid.ToString(),
            TypeKind = LibraryResolver.TypeKind(type),
            RuntimeType = LibraryReflection.RuntimeType(type),
            Versions = includeVersions ? LibraryResolver.Versions(type).Select(v => LibraryResolver.Summary(v, unreadable)).ToList() : null,
            Unreadable = unreadable.Distinct(StringComparer.Ordinal).ToList(),
        };
    }

    private static LibraryTreeEntryInfo MasterCopyEntry(MasterCopy masterCopy, IReadOnlyList<string> folder, int depth)
    {
        var unreadable = new List<string>();
        string? name;
        try
        {
            name = masterCopy.Name;
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
        {
            name = null;
            unreadable.Add("MasterCopy.Name");
        }

        return new LibraryTreeEntryInfo
        {
            Kind = LibraryPolicy.EntryKinds.MasterCopy,
            Path = LibraryPolicy.FormatPath(folder.Concat(new[] { name ?? "?" })),
            Name = name,
            Depth = depth,
            RuntimeType = LibraryReflection.RuntimeType(masterCopy),
            ContentTypes = ContentTypes(masterCopy, unreadable),
            Unreadable = unreadable,
        };
    }

    /// <summary>G: <c>mc.ContentDescriptions[].ContentType</c> as text (types undocumented → runtime lookup).</summary>
    internal static List<string> ContentTypes(MasterCopy masterCopy, List<string> unreadable)
        => LibraryReflection.Enumerate(LibraryReflection.Optional(masterCopy, "ContentDescriptions", unreadable, "MasterCopy.ContentDescriptions"),
                "master copy content descriptions")
            .Select(description => LibraryReflection.Display(LibraryReflection.Optional(description, "ContentType", unreadable,
                "MasterCopyContentDescription.ContentType")))
            .Where(text => text is not null).Select(text => text!).ToList();

    private static LibraryTypeDetailInfo TypeDetail(ResolvedLibrary library, LibraryRequestInfo request)
    {
        var (type, path) = LibraryResolver.ResolveType(library.Library, request.TypePath, request.TypeGuid);
        var result = new LibraryTypeDetailInfo
        {
            Library = library.Ref, Path = path, Name = LibraryResolver.SafeName(type), Guid = type.Guid.ToString(),
            TypeKind = LibraryResolver.TypeKind(type), RuntimeType = LibraryReflection.RuntimeType(type),
        };
        foreach (var version in LibraryResolver.Versions(type))
        {
            var unreadable = new List<string>();
            var summary = LibraryResolver.Summary(version, unreadable);
            result.Versions.Add(new LibraryVersionDetailInfo
            {
                VersionNumber = summary.VersionNumber, Guid = summary.Guid, State = summary.State, IsDefault = summary.IsDefault,
                Dependencies = References(version, "Dependencies", unreadable),
                Dependents = References(version, "Dependents", unreadable),
                MasterCopiesContainingInstances = LibraryReflection.Enumerate(LibraryReflection.Optional(version, "MasterCopiesContainingInstances",
                        unreadable, "LibraryTypeVersion.MasterCopiesContainingInstances"), "master copies containing instances")
                    .Take(LibraryPolicy.MaxReferences)
                    .Select(candidate => candidate is MasterCopy masterCopy ? masterCopy.Name : LibraryReflection.Display(candidate))
                    .Where(text => text is not null).Select(text => text!).ToList(),
                OriginalLibrary = OriginalLibrary(version, unreadable),
                Unreadable = unreadable.Distinct(StringComparer.Ordinal).ToList(),
            });
        }

        TypeProperties(type, result);
        return result;
    }

    /// <summary>L names <c>OriginalLibrary</c> without its type: a library object is reported by its path (G: <c>Path</c>), anything else as text.</summary>
    private static string? OriginalLibrary(LibraryTypeVersion version, List<string> unreadable)
    {
        var original = LibraryReflection.Optional(version, "OriginalLibrary", unreadable, "LibraryTypeVersion.OriginalLibrary");
        if (original is null) return null;
        return LibraryReflection.Display(LibraryReflection.Optional(original, "Path", new List<string>(), "OriginalLibrary.Path"))
            ?? LibraryReflection.Display(original);
    }

    internal static List<LibraryVersionRefInfo> References(LibraryTypeVersion version, string property, List<string> unreadable)
        => LibraryReflection.Enumerate(LibraryReflection.Optional(version, property, unreadable, "LibraryTypeVersion." + property), property)
            .Take(LibraryPolicy.MaxReferences).Select(LibraryResolver.Reference).ToList();

    /// <summary><c>LibraryTypeVersion.FindInstances(IInstanceSearchScope)</c> with the PLC software as scope (L: PlcSoftware is a supported scope).</summary>
    private static LibraryTypeInstancesInfo Instances(ResolvedLibrary library, ProjectBase project, LibraryRequestInfo request)
    {
        var plc = PlcSoftwareLocator.FindUnique(project, request.PlcName);
        var scope = plc.Software as IInstanceSearchScope ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
            "This PLC software is not an IInstanceSearchScope on this TIA Portal installation.");
        LibraryType type;
        string? typePath = null;
        IReadOnlyList<LibraryTypeVersion> versions;
        if (request.Version is not null || request.VersionGuid is not null)
        {
            var resolved = LibraryResolver.ResolveVersion(library.Library, request);
            type = resolved.Type ?? throw new WorkerOperationException(WorkerFailureCategories.TargetEvidenceMismatch, "The version's owning type is unreadable.");
            typePath = resolved.Path;
            versions = new[] { resolved.Version };
        }
        else
        {
            (type, typePath) = LibraryResolver.ResolveType(library.Library, request.TypePath, request.TypeGuid);
            versions = LibraryResolver.Versions(type);
        }

        var result = new LibraryTypeInstancesInfo
        {
            Library = library.Ref, TypeName = LibraryResolver.SafeName(type), TypeGuid = type.Guid.ToString(),
            PlcName = plc.Software.Name ?? string.Empty, DeviceName = plc.DeviceName,
        };
        foreach (var version in versions)
        {
            var found = LibraryReflection.Invoke(version, "FindInstances", new[] { typeof(IInstanceSearchScope) }, new object[] { scope },
                "LibraryTypeVersion.FindInstances(IInstanceSearchScope)");
            var entry = new LibraryVersionInstancesInfo { VersionNumber = LibraryResolver.VersionText(version, new List<string>()), Guid = version.Guid.ToString(), Complete = true };
            foreach (var instance in LibraryReflection.Enumerate(found, "type instances"))
            {
                if (entry.Instances.Count >= LibraryPolicy.MaxInstances) { entry.Complete = false; break; }
                entry.Instances.Add(new LibraryInstanceInfo
                {
                    Name = LibraryReflection.Text(instance, "Name", new List<string>(), "instance name"),
                    RuntimeType = LibraryReflection.RuntimeType(instance),
                });
            }

            result.Versions.Add(entry);
        }

        return result;
    }

    /// <summary>G: <c>block.GetService&lt;LibraryTypeInstanceInfo&gt;()?.LibraryTypeVersion</c>; null means not a type instance.</summary>
    private static LibraryInstanceLinkInfo InstanceLink(ProjectBase project, LibraryRequestInfo request)
    {
        IEngineeringServiceProvider provider;
        var result = new LibraryInstanceLinkInfo();
        if (request.BlockPath is not null)
        {
            var address = BlockAddress.Parse(request.BlockPath);
            var block = BlockTargetResolver.ResolveForExport(project, address).Block
                ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Block '{request.BlockPath}' was not found.");
            provider = block;
            result.ObjectKind = LibraryPolicy.SourceKinds.Block;
            result.ObjectName = block.Name;
            result.PlcName = address.PlcName;
        }
        else
        {
            var address = PlcTypeAddress.Parse(request.PlcTypePath!);
            var type = PlcTypeTargetResolver.ResolveForExport(project, address).Type
                ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"PLC data type '{request.PlcTypePath}' was not found.");
            provider = type;
            result.ObjectKind = LibraryPolicy.SourceKinds.PlcType;
            result.ObjectName = type.Name;
            result.PlcName = address.PlcName;
        }

        var info = provider.GetService<LibraryTypeInstanceInfo>();
        if (info is null) return result;
        result.Linked = true;
        var version = LibraryReflection.Optional(info, "LibraryTypeVersion", result.Unreadable, "LibraryTypeInstanceInfo.LibraryTypeVersion") as LibraryTypeVersion;
        if (version is null) return result;
        var owner = ((IEngineeringInstance)version).Parent as LibraryType;
        result.TypeName = owner is null ? null : LibraryResolver.SafeName(owner);
        result.TypeGuid = owner?.Guid.ToString();
        result.VersionNumber = LibraryResolver.VersionText(version, result.Unreadable);
        result.VersionGuid = version.Guid.ToString();
        result.VersionState = version.State.ToString();
        return result;
    }

    /// <summary>L: <c>globalLibrary.UpdateCheck(project, UpdateCheckMode.ReportOutOfDateOnly)</c> → <c>UpdateCheckResult.Messages</c> (recursive).</summary>
    private static LibraryUpdateCheckInfo UpdateCheck(ResolvedLibrary library, ProjectBase project, LibraryRequestInfo request)
    {
        var mode = request.UpdateCheckMode ?? LibraryPolicy.DefaultUpdateCheckMode;
        var bound = project as Siemens.Engineering.Project ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
            "update_check compares a library with a standalone project.");
        var check = LibraryReflection.Invoke(library.Raw, "UpdateCheck", new[] { typeof(Siemens.Engineering.Project), typeof(UpdateCheckMode) },
            new[] { bound, LibraryReflection.EnumValue(typeof(UpdateCheckMode), mode, "UpdateCheckMode") }, "UpdateCheck(Project, UpdateCheckMode)");
        var result = new LibraryUpdateCheckInfo { Library = library.Ref, Mode = mode, Complete = true };
        var count = 0;
        result.Messages = Messages(LibraryReflection.Optional(check, "Messages", new List<string>(), "UpdateCheckResult.Messages"), 1);
        result.MessageCount = count;
        return result;

        List<LibraryMessageInfo> Messages(object? collection, int depth)
        {
            var list = new List<LibraryMessageInfo>();
            foreach (var message in LibraryReflection.Enumerate(collection, "update check messages"))
            {
                if (count >= LibraryPolicy.MaxMessages) { result.Complete = false; break; }
                count++;
                var entry = new LibraryMessageInfo
                {
                    Description = LibraryReflection.Display(LibraryReflection.Optional(message, "Description", new List<string>(), "UpdateCheckResultMessage.Description")),
                    MessageParts = Parts(LibraryReflection.Optional(message, "MessageParts", new List<string>(), "UpdateCheckResultMessage.MessageParts")),
                };
                var nested = LibraryReflection.Optional(message, "Messages", new List<string>(), "UpdateCheckResultMessage.Messages");
                if (depth >= LibraryPolicy.MaxMessageDepth)
                {
                    if (LibraryReflection.Enumerate(nested, "nested messages").Any()) result.Complete = false;
                }
                else entry.Messages = Messages(nested, depth + 1);
                list.Add(entry);
            }

            return list;
        }
    }

    private static List<string> Parts(object? parts)
    {
        if (parts is null || parts is string) return parts is string text ? new List<string> { LibraryPolicy.Bound(text)! } : new List<string>();
        if (parts is System.Collections.IDictionary dictionary)
            return dictionary.Keys.Cast<object>().Take(LibraryPolicy.MaxAttributes)
                .Select(key => $"{LibraryReflection.Display(key)}: {LibraryReflection.Display(dictionary[key])}").ToList();
        return LibraryReflection.Enumerate(parts, "message parts").Take(LibraryPolicy.MaxAttributes)
            .Select(part => LibraryReflection.Display(part) ?? string.Empty).ToList();
    }

    /// <summary>
    /// L: <c>CompareToLibrary(ISoftwareCompareTarget)</c> on the project or global library, then
    /// <c>RootElement</c>, child <c>Elements</c>, <c>ComparisonResult</c> and <c>DetailedInformation.Properties</c>.
    /// Element identities are not named by the sources, so each element and detail is projected schema-free.
    /// </summary>
    private static LibraryCompareInfo Compare(ResolvedLibrary library, ResolvedLibrary other, LibraryRequestInfo request)
    {
        if (ReferenceEquals(library.Raw, other.Raw) || library.Raw.Equals(other.Raw))
            throw LibraryResolver.Validation("compareTo names the compared library itself; name another library.");
        var target = other.Raw as ISoftwareCompareTarget ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
            $"'{other.Ref.Library}' is not an ISoftwareCompareTarget on this TIA Portal installation.");
        var compared = LibraryReflection.Invoke(library.Raw, "CompareToLibrary", new[] { typeof(ISoftwareCompareTarget) }, new object[] { target },
            "CompareToLibrary(ISoftwareCompareTarget)");
        var result = new LibraryCompareInfo { Library = library.Ref, CompareTo = other.Ref, MaxDepth = request.MaxDepth, MaxElements = request.MaxElements, Complete = true };
        var root = LibraryReflection.Optional(compared, "RootElement", new List<string>(), "LibraryCompareResult.RootElement");
        if (root is null) return result;
        result.Root = Element(root, 1);
        return result;

        LibraryCompareElementInfo Element(object element, int depth)
        {
            result.ElementsListed++;
            var info = new LibraryCompareElementInfo
            {
                ComparisonResult = LibraryReflection.Display(LibraryReflection.Optional(element, "ComparisonResult", new List<string>(), "ComparisonResult")),
                Attributes = LibraryReflection.Attributes(element),
            };
            var detailed = LibraryReflection.Optional(element, "DetailedInformation", new List<string>(), "DetailedInformation");
            foreach (var property in LibraryReflection.Enumerate(LibraryReflection.Optional(detailed, "Properties", new List<string>(), "DetailedInformation.Properties"), "detailed compare properties").Take(LibraryPolicy.MaxAttributes))
                info.Details.Add(new LibraryCompareDetailInfo { Attributes = LibraryReflection.Attributes(property) });
            var children = LibraryReflection.Enumerate(LibraryReflection.Optional(element, "Elements", new List<string>(), "Elements"), "compare elements").ToArray();
            if (children.Length == 0) return info;
            if (depth >= request.MaxDepth) { Truncate(info, $"maxDepth {request.MaxDepth}"); return info; }
            foreach (var child in children)
            {
                if (result.ElementsListed >= request.MaxElements) { Truncate(info, $"maxElements {request.MaxElements}"); break; }
                info.Elements.Add(Element(child, depth + 1));
            }

            return info;
        }

        void Truncate(LibraryCompareElementInfo info, string cap)
        {
            info.ElementsTruncated = true;
            result.Complete = false;
            result.Omission ??= cap + " cut the compare tree";
        }
    }
}
