using Siemens.Engineering;
using Siemens.Engineering.Library;
using Siemens.Engineering.Library.MasterCopies;
using Siemens.Engineering.Library.Types;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Libraries;

internal sealed record ResolvedLibrary(ILibrary Library, object Raw, LibraryRefInfo Ref);

/// <summary>
/// Resolves the library selectors, type folders, types, versions and master copies of the library tools.
/// Compiled members are the documented ones (<c>Project.ProjectLibrary</c>, <c>TiaPortal.GlobalLibraries</c>,
/// <c>ILibrary.TypeFolder/MasterCopyFolder/FindType/FindVersion</c>, <c>LibraryType.Name/Guid</c>,
/// <c>LibraryTypeVersion.Guid/State</c>, <c>MasterCopyComposition.Find</c>); the folder and version collections,
/// names of folders and global libraries and version numbers are runtime lookups by their documented names.
/// </summary>
internal static class LibraryResolver
{
    public static ResolvedLibrary Resolve(TiaPortal portal, ProjectBase project, string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
            throw Validation("A library selector is required: 'project' or the path of an open global library as list_libraries reports it.");
        if (LibraryPolicy.IsProject(selector))
        {
            var library = (project as Siemens.Engineering.Project)?.ProjectLibrary ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
                "The bound project offers no project library.");
            return new ResolvedLibrary(library, library, ProjectRef(library));
        }

        var matches = OpenGlobals(portal, new List<string>()).Where(g => LibraryPolicy.SamePath(g.Ref.Path, selector)).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                $"No open global library has the path '{selector}'. List the open libraries with library_read list_libraries, or open it with library_write open_global_library."),
            _ => throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous,
                $"{matches.Length} open global libraries have the path '{selector}'."),
        };
    }

    public static LibraryRefInfo ProjectRef(ProjectLibrary library) => new()
    {
        Library = LibraryPolicy.ProjectLibrary,
        Kind = LibraryPolicy.LibraryKinds.Project,
        Path = null,
        Name = null,
        RuntimeType = LibraryReflection.RuntimeType(library),
    };

    /// <summary>
    /// The open global libraries: enumeration of <c>TiaPortal.GlobalLibraries</c> (G: <c>OfType&lt;GlobalLibrary&gt;()</c>),
    /// each with <c>Path</c> (G: <c>Path.FullName</c>) and its name when readable.
    /// </summary>
    public static IReadOnlyList<ResolvedLibrary> OpenGlobals(TiaPortal portal, List<string> unreadable)
    {
        var result = new List<ResolvedLibrary>();
        foreach (var candidate in LibraryReflection.Enumerate(portal.GlobalLibraries, "open global libraries").OfType<GlobalLibrary>())
        {
            var path = LibraryReflection.Display(LibraryReflection.Optional(candidate, "Path", unreadable, "GlobalLibrary.Path"));
            if (path is null) continue;
            result.Add(new ResolvedLibrary(candidate, candidate, new LibraryRefInfo
            {
                Library = path,
                Kind = candidate switch
                {
                    UserGlobalLibrary => LibraryPolicy.LibraryKinds.User,
                    SystemGlobalLibrary => LibraryPolicy.LibraryKinds.System,
                    CorporateGlobalLibrary => LibraryPolicy.LibraryKinds.Corporate,
                    _ => LibraryPolicy.LibraryKinds.Global,
                },
                Path = path,
                Name = LibraryReflection.Text(candidate, "Name", new List<string>(), "GlobalLibrary.Name"),
                RuntimeType = LibraryReflection.RuntimeType(candidate),
            }));
        }

        return result;
    }

    /// <summary>
    /// The full path of a requested library file: <c>new FileInfo(path).FullName</c>, which resolves <c>.</c> segments and
    /// duplicate or forward separators, so it compares with an open library's <c>Path.FullName</c> (G: "Check for an
    /// already-open library first (by comparing <c>GlobalLibrary.Path.FullName</c>)", ordinal case-insensitive).
    /// </summary>
    public static string FullPath(string path, string field)
    {
        try
        {
            return new FileInfo(path).FullName;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            throw Validation($"{field} '{path}' is not a usable file path: {exception.Message}");
        }
    }

    /// <summary>The open global library whose <c>Path.FullName</c> equals the requested file's full path (G), or null.</summary>
    public static ResolvedLibrary? FindOpenFile(IReadOnlyList<ResolvedLibrary> open, string path, string field)
    {
        var full = FullPath(path, field);
        return open.FirstOrDefault(g => LibraryPolicy.SamePath(g.Ref.Path, full));
    }

    /// <summary>
    /// The user global library <paramref name="library"/> names, or a <c>target_kind_unsupported</c> refusal before anything is
    /// called. Save and close are sourced only for <c>UserGlobalLibrary</c> (L: "A <c>UserGlobalLibrary</c> opened by the
    /// application must be saved when intended and closed deterministically with <c>Close()</c>"; A: <c>UserGlobalLibrary.Close()</c>);
    /// no source shows them on a system or corporate global library.
    /// </summary>
    public static UserGlobalLibrary RequireUserGlobal(ResolvedLibrary library, string operation, string? reason = null)
        => library.Raw as UserGlobalLibrary ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
            $"{operation} acts on a user global library; '{library.Ref.Library}' is a {library.Ref.Kind} library "
            + $"({reason ?? "system and corporate libraries are read-only"}).");

    /// <summary>Why close_global_library refuses a system or corporate global library.</summary>
    public const string CloseSourcedForUserLibrariesOnly =
        "the sources document Close() for UserGlobalLibrary only; close a system or corporate library in TIA Portal";

    // ---------------------------------------------------------------- type folders

    /// <summary>Child user folders through the base-class <c>LibraryTypeFolder.Folders</c> (G: never the derived shadow).</summary>
    public static IReadOnlyList<LibraryTypeUserFolder> ChildFolders(LibraryTypeFolder folder)
        => LibraryReflection.Enumerate(LibraryReflection.Required(folder, typeof(LibraryTypeFolder), "Folders", "LibraryTypeFolder.Folders"),
            "type folders").OfType<LibraryTypeUserFolder>().ToArray();

    /// <summary>Direct child types through <c>LibraryTypeFolder.Types</c> (G: direct children only).</summary>
    public static IReadOnlyList<LibraryType> Types(LibraryTypeFolder folder)
        => LibraryReflection.Enumerate(LibraryReflection.Required(folder, typeof(LibraryTypeFolder), "Types", "LibraryTypeFolder.Types"),
            "library types").OfType<LibraryType>().ToArray();

    public static string? FolderName(LibraryTypeFolder folder, List<string> unreadable)
        => LibraryReflection.Text(folder, "Name", unreadable, "LibraryTypeUserFolder.Name");

    public static LibraryTypeFolder ResolveFolder(ILibrary library, IReadOnlyList<string> segments)
    {
        LibraryTypeFolder current = library.TypeFolder;
        var walked = new List<string>();
        foreach (var segment in segments)
        {
            var matches = ChildFolders(current).Where(child => string.Equals(FolderName(child, new List<string>()), segment, StringComparison.OrdinalIgnoreCase)).ToArray();
            walked.Add(segment);
            current = matches.Length switch
            {
                1 => matches[0],
                0 => throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Type folder '{LibraryPolicy.FormatPath(walked)}' was not found."),
                _ => throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous, $"Type folder '{LibraryPolicy.FormatPath(walked)}' is ambiguous ({matches.Length} folders)."),
            };
        }

        return current;
    }

    public static LibraryTypeFolder ResolveFolder(ILibrary library, string? folderPath)
        => ResolveFolder(library, LibraryPolicy.ParseFolderPath(folderPath, out var error) ?? throw Validation($"folderPath: {error}"));

    // ---------------------------------------------------------------- types and versions

    public static (LibraryType Type, string? Path) ResolveType(ILibrary library, string? typePath, string? typeGuid)
    {
        if (typeGuid is not null)
        {
            var guid = ParseGuid(typeGuid, "typeGuid");
            var found = library.FindType(guid) ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                $"No library type has the GUID {typeGuid} (ILibrary.FindType).");
            return (found, null);
        }

        var parsed = LibraryPolicy.ParseTypePath(typePath, out var error) ?? throw Validation($"typePath: {error}");
        var folder = ResolveFolder(library, parsed.Folder);
        var matches = Types(folder).Where(type => string.Equals(SafeName(type), parsed.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
        var path = LibraryPolicy.FormatPath(parsed.Folder.Concat(new[] { parsed.Name }));
        return matches.Length switch
        {
            1 => (matches[0], LibraryPolicy.FormatPath(parsed.Folder.Concat(new[] { SafeName(matches[0]) ?? parsed.Name }))),
            0 => throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Library type '{path}' was not found."),
            _ => throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous, $"Library type '{path}' is ambiguous ({matches.Length} types); address it by typeGuid."),
        };
    }

    public static string? SafeName(LibraryType type)
    {
        try
        {
            return type.Name;
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
        {
            return null;
        }
    }

    /// <summary>Every version through <c>LibraryType.Versions</c> (L: <c>foreach (LibraryTypeVersion version in type.Versions)</c>).</summary>
    public static IReadOnlyList<LibraryTypeVersion> Versions(LibraryType type)
        => LibraryReflection.Enumerate(LibraryReflection.Required(type, "Versions", "LibraryType.Versions"), "type versions")
            .OfType<LibraryTypeVersion>().ToArray();

    public static string? VersionText(LibraryTypeVersion version, List<string> unreadable)
        => LibraryReflection.Display(LibraryReflection.Optional(version, "VersionNumber", unreadable, "LibraryTypeVersion.VersionNumber"));

    public static LibraryVersionSummaryInfo Summary(LibraryTypeVersion version, List<string> unreadable) => new()
    {
        VersionNumber = VersionText(version, unreadable),
        Guid = version.Guid.ToString(),
        State = version.State.ToString(),
        IsDefault = LibraryReflection.Optional(version, "IsDefault", unreadable, "LibraryTypeVersion.IsDefault") as bool?,
    };

    public static (LibraryType? Type, string? Path, LibraryTypeVersion Version) ResolveVersion(ILibrary library, LibraryRequestInfo request)
    {
        if (request.VersionGuid is not null)
        {
            var version = library.FindVersion(ParseGuid(request.VersionGuid, "versionGuid")) ?? throw new WorkerOperationException(
                WorkerFailureCategories.TargetNotFound, $"No library type version has the GUID {request.VersionGuid} (ILibrary.FindVersion).");
            var owner = ((IEngineeringInstance)version).Parent as LibraryType;
            if (request.TypePath is not null || request.TypeGuid is not null)
            {
                var (type, path) = ResolveType(library, request.TypePath, request.TypeGuid);
                if (owner is null || !owner.Equals(type))
                    throw new WorkerOperationException(WorkerFailureCategories.TargetEvidenceMismatch,
                        $"Version {request.VersionGuid} does not belong to the addressed type.");
                return (type, path, version);
            }

            return (owner, null, version);
        }

        var resolved = ResolveType(library, request.TypePath, request.TypeGuid);
        var versions = Versions(resolved.Type);
        if (request.Version is null)
        {
            return versions.Count == 1
                ? (resolved.Type, resolved.Path, versions[0])
                : throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous,
                    $"Type '{resolved.Path ?? SafeName(resolved.Type)}' has {versions.Count} versions ({string.Join(", ", versions.Select(v => VersionText(v, new List<string>()) ?? v.Guid.ToString()))}); name one with version or versionGuid.");
        }

        var matches = versions.Where(v => string.Equals(VersionText(v, new List<string>()), request.Version, StringComparison.Ordinal)).ToArray();
        return matches.Length switch
        {
            1 => (resolved.Type, resolved.Path, matches[0]),
            0 => throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                $"Type '{resolved.Path ?? SafeName(resolved.Type)}' has no version '{request.Version}'."),
            _ => throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous,
                $"Type '{resolved.Path ?? SafeName(resolved.Type)}' has {matches.Length} versions '{request.Version}'; use versionGuid."),
        };
    }

    public static string TypeKind(LibraryType type)
        => type is CodeBlockLibraryType ? LibraryPolicy.TypeKinds.CodeBlock
            : type is PlcTypeLibraryType ? LibraryPolicy.TypeKinds.PlcType : LibraryPolicy.TypeKinds.Other;

    public static LibraryVersionRefInfo Reference(object? candidate)
    {
        var version = candidate as LibraryTypeVersion;
        var owner = version is null ? null : ((IEngineeringInstance)version).Parent as LibraryType;
        return new LibraryVersionRefInfo
        {
            TypeName = owner is null ? null : SafeName(owner),
            VersionNumber = version is null ? LibraryReflection.Display(candidate) : VersionText(version, new List<string>()),
            Guid = version?.Guid.ToString(),
        };
    }

    // ---------------------------------------------------------------- master copies

    public static MasterCopy ResolveMasterCopy(ILibrary library, string? name, string? folderPath = null)
    {
        if (LibraryPolicy.NameError(name) is { } error) throw Validation($"masterCopyName {error}");
        var segments = Segments(folderPath);
        var folder = ResolveMasterCopyFolder(library, segments);
        return folder.MasterCopies.Find(name!) ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
            $"No master copy named '{name}' is in the master copy folder {(segments.Count == 0 ? "(root)" : LibraryPolicy.FormatPath(segments))}.");
    }

    /// <summary>Child user folders through <c>MasterCopyFolder.Folders</c> (V21: "Composition of MasterCopy user folders"; runtime lookup).</summary>
    public static IReadOnlyList<MasterCopyFolder> MasterCopyChildFolders(MasterCopyFolder folder)
        => LibraryReflection.Enumerate(LibraryReflection.Required(folder, typeof(MasterCopyFolder), "Folders", "MasterCopyFolder.Folders"),
            "master copy folders").OfType<MasterCopyFolder>().ToArray();

    /// <summary>The <c>Folders</c> composition object of a master copy folder (for <c>Create(String)</c>/<c>Find(String)</c>).</summary>
    public static object MasterCopyFolderComposition(MasterCopyFolder folder)
        => LibraryReflection.Required(folder, typeof(MasterCopyFolder), "Folders", "MasterCopyFolder.Folders")
            ?? throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability, "MasterCopyFolder.Folders returned nothing.");

    public static string? MasterCopyFolderName(MasterCopyFolder folder, List<string> unreadable)
        => LibraryReflection.Text(folder, "Name", unreadable, "MasterCopyUserFolder.Name");

    /// <summary>The master copy folder at <paramref name="segments"/> below the root master copy folder (names case-insensitive, unique).</summary>
    public static MasterCopyFolder ResolveMasterCopyFolder(ILibrary library, IReadOnlyList<string> segments)
    {
        MasterCopyFolder current = library.MasterCopyFolder;
        var walked = new List<string>();
        foreach (var segment in segments)
        {
            walked.Add(segment);
            var matches = MasterCopyChildFolders(current)
                .Where(child => string.Equals(MasterCopyFolderName(child, new List<string>()), segment, StringComparison.OrdinalIgnoreCase)).ToArray();
            current = matches.Length switch
            {
                1 => matches[0],
                0 => throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Master copy folder '{LibraryPolicy.FormatPath(walked)}' was not found."),
                _ => throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous, $"Master copy folder '{LibraryPolicy.FormatPath(walked)}' is ambiguous ({matches.Length} folders)."),
            };
        }

        return current;
    }

    public static MasterCopyFolder ResolveMasterCopyFolder(ILibrary library, string? folderPath) => ResolveMasterCopyFolder(library, Segments(folderPath));

    // ---------------------------------------------------------------- PLC groups

    public static PlcBlockGroup BlockGroup(PlcSoftwareLocator.DiscoveredPlcSoftware plc, string? folderPath)
        => BlockTargetResolver.FindBlockGroup(plc.Software.BlockGroup, Segments(folderPath));

    public static PlcTypeGroup TypeGroup(PlcSoftwareLocator.DiscoveredPlcSoftware plc, string? folderPath)
    {
        PlcTypeGroup current = plc.Software.TypeGroup;
        var walked = new List<string>();
        foreach (var segment in Segments(folderPath))
        {
            walked.Add(segment);
            var matches = current.Groups.Where(group => string.Equals(group.Name, segment, StringComparison.OrdinalIgnoreCase)).ToArray();
            current = matches.Length == 1 ? matches[0] : throw new WorkerOperationException(
                matches.Length == 0 ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous,
                $"PLC data type folder '{LibraryPolicy.FormatPath(walked)}' was {(matches.Length == 0 ? "not found" : "ambiguous")}.");
        }

        return current;
    }

    public static PlcTagTableGroup TagTableGroup(PlcSoftwareLocator.DiscoveredPlcSoftware plc, string? folderPath)
    {
        PlcTagTableGroup current = plc.Software.TagTableGroup;
        var walked = new List<string>();
        foreach (var segment in Segments(folderPath))
        {
            walked.Add(segment);
            current = current.Groups.Find(segment) ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                $"Tag table folder '{LibraryPolicy.FormatPath(walked)}' was not found.");
        }

        return current;
    }

    public static IReadOnlyList<string> Segments(string? folderPath)
        => LibraryPolicy.ParseFolderPath(folderPath, out var error) ?? throw Validation($"folderPath: {error}");

    public static Guid ParseGuid(string text, string field)
        => Guid.TryParse(text, out var guid) ? guid : throw Validation($"{field} '{text}' is not a GUID.");

    public static WorkerOperationException Validation(string message) => new(WorkerFailureCategories.ValidationError, message);
}
