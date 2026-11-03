using Siemens.Engineering;
using Siemens.Engineering.Library.MasterCopies;
using Siemens.Engineering.Library.Types;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Libraries;

/// <summary>
/// The library_read operations added from the V21 API description (issue #17): <c>LibraryType.GetSupportedExportFormats()</c> and
/// the detailed compares <c>LibraryType.CompareTo(LibraryType)</c>, <c>LibraryTypeVersion.CompareTo(LibraryTypeVersion)</c>,
/// <c>MasterCopy.CompareTo(MasterCopy)</c> (<c>DetailedCompareResult.Properties</c> of <c>DetailedCompareResultElement</c>), and
/// the type properties of get_type. All bound at runtime by documented name; nothing changes a library. Not live-tested.
/// </summary>
internal static partial class LibraryReadService
{
    private static LibraryExportFormatsInfo ExportFormats(ResolvedLibrary library, LibraryRequestInfo request)
    {
        var (type, path) = LibraryResolver.ResolveType(library.Library, request.TypePath, request.TypeGuid);
        var formats = LibraryReflection.Invoke(type, "GetSupportedExportFormats", Type.EmptyTypes, Array.Empty<object>(), "LibraryType.GetSupportedExportFormats()");
        return new LibraryExportFormatsInfo
        {
            Library = library.Ref, Path = path, Name = LibraryResolver.SafeName(type), Guid = type.Guid.ToString(),
            Formats = LibraryReflection.Enumerate(formats, "export formats").Take(LibraryPolicy.MaxReferences)
                .Select(format => LibraryReflection.Display(format) ?? string.Empty).ToList(),
        };
    }

    private static LibraryDetailedCompareInfo CompareObjects(TiaPortal portal, ProjectBase project, LibraryRequestInfo request)
    {
        var library = LibraryResolver.Resolve(portal, project, request.Library);
        var other = request.TargetLibrary is null ? library : LibraryResolver.Resolve(portal, project, request.TargetLibrary);
        object left, right;
        string leftText, rightText, documented;
        switch (request.Operation)
        {
            case LibraryMethods.CompareTypes:
            {
                var (type, path) = LibraryResolver.ResolveType(library.Library, request.TypePath, request.TypeGuid);
                var (otherType, otherPath) = LibraryResolver.ResolveType(other.Library, request.TargetTypePath, request.TargetTypeGuid);
                (left, right) = (type, otherType);
                (leftText, rightText) = ($"type {path ?? type.Guid.ToString()}", $"type {otherPath ?? otherType.Guid.ToString()}");
                documented = "LibraryType.CompareTo(LibraryType)";
                break;
            }
            case LibraryMethods.CompareTypeVersions:
            {
                var (_, path, version) = LibraryResolver.ResolveVersion(library.Library, request);
                var (_, otherPath, otherVersion) = LibraryResolver.ResolveVersion(other.Library, new LibraryRequestInfo
                {
                    TypePath = request.TargetTypePath, TypeGuid = request.TargetTypeGuid, Version = request.TargetVersion, VersionGuid = request.TargetVersionGuid,
                });
                (left, right) = (version, otherVersion);
                leftText = $"type {path ?? request.TypeGuid ?? "?"} version {LibraryResolver.VersionText(version, new List<string>()) ?? version.Guid.ToString()}";
                rightText = $"type {otherPath ?? request.TargetTypeGuid ?? "?"} version {LibraryResolver.VersionText(otherVersion, new List<string>()) ?? otherVersion.Guid.ToString()}";
                documented = "LibraryTypeVersion.CompareTo(LibraryTypeVersion)";
                break;
            }
            default:
            {
                var masterCopy = LibraryResolver.ResolveMasterCopy(library.Library, request.MasterCopyName, request.MasterCopyFolderPath);
                var otherMasterCopy = LibraryResolver.ResolveMasterCopy(other.Library, request.TargetMasterCopyName, request.TargetMasterCopyFolderPath);
                (left, right) = (masterCopy, otherMasterCopy);
                leftText = $"master copy {LibraryPolicy.FormatPath(LibraryResolver.Segments(request.MasterCopyFolderPath).Concat(new[] { request.MasterCopyName! }))}";
                rightText = $"master copy {LibraryPolicy.FormatPath(LibraryResolver.Segments(request.TargetMasterCopyFolderPath).Concat(new[] { request.TargetMasterCopyName! }))}";
                documented = "MasterCopy.CompareTo(MasterCopy)";
                break;
            }
        }

        if (left.Equals(right)) throw LibraryResolver.Validation("the compared object is the object itself; name another one.");
        var parameter = request.Operation switch
        {
            LibraryMethods.CompareTypes => typeof(LibraryType),
            LibraryMethods.CompareTypeVersions => typeof(LibraryTypeVersion),
            _ => typeof(MasterCopy),
        };
        var compared = LibraryReflection.Invoke(left, "CompareTo", new[] { parameter }, new[] { right }, documented);
        var result = new LibraryDetailedCompareInfo { Library = library.Ref, Left = leftText, CompareTo = other.Ref, Right = rightText, Complete = true };
        var properties = LibraryReflection.Enumerate(LibraryReflection.Optional(compared, "Properties", result.Unreadable, "DetailedCompareResult.Properties"),
            "detailed compare properties");
        foreach (var property in properties)
        {
            if (result.Properties.Count >= request.MaxElements) { result.Complete = false; break; }
            result.Properties.Add(new LibraryDetailedCompareEntryInfo
            {
                Description = LibraryReflection.Display(LibraryReflection.Optional(property, "Description", result.Unreadable, "DetailedCompareResultElement.Description")),
                Status = LibraryReflection.Display(LibraryReflection.Optional(property, "DetailCompareStatus", result.Unreadable, "DetailedCompareResultElement.DetailCompareStatus")),
                LeftValue = LibraryReflection.Display(LibraryReflection.Optional(property, "LeftValue", result.Unreadable, "DetailedCompareResultElement.LeftValue")),
                RightValue = LibraryReflection.Display(LibraryReflection.Optional(property, "RightValue", result.Unreadable, "DetailedCompareResultElement.RightValue")),
            });
        }

        result.Unreadable = result.Unreadable.Distinct(StringComparer.Ordinal).ToList();
        return result;
    }

    /// <summary>
    /// The <c>LibraryTypeInstanceInfo</c> service of a PLC block or data type (G: <c>GetService&lt;LibraryTypeInstanceInfo&gt;()</c>), for
    /// <c>LibraryTypeVersion.Edit(LibraryTypeInstanceInfo)</c>; an object that is no type instance is <c>target_kind_unsupported</c>.
    /// </summary>
    internal static LibraryTypeInstanceInfo InstanceInfoOf(ProjectBase project, string? blockPath, string? plcTypePath)
    {
        IEngineeringServiceProvider provider = blockPath is not null
            ? BlockTargetResolver.ResolveForExport(project, BlockAddress.Parse(blockPath)).Block
                ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Block '{blockPath}' was not found.")
            : PlcTypeTargetResolver.ResolveForExport(project, PlcTypeAddress.Parse(plcTypePath!)).Type
                ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"PLC data type '{plcTypePath}' was not found.");
        return provider.GetService<LibraryTypeInstanceInfo>() ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
            $"'{blockPath ?? plcTypePath}' is not an instance of a library type (no LibraryTypeInstanceInfo).");
    }

    /// <summary>The V21 type properties Author, Comment, DoNotUse, Namespace, SetForUpdate, Status (runtime lookups; unreadable ones are null).</summary>
    private static void TypeProperties(LibraryType type, LibraryTypeDetailInfo result)
    {
        result.DoNotUse = LibraryReflection.Optional(type, "DoNotUse", result.Unreadable, "LibraryType.DoNotUse") as bool?;
        result.SetForUpdate = LibraryReflection.Optional(type, "SetForUpdate", result.Unreadable, "LibraryType.SetForUpdate") as bool?;
        result.Status = LibraryReflection.Display(LibraryReflection.Optional(type, "Status", result.Unreadable, "LibraryType.Status"));
        result.Namespace = LibraryReflection.Display(LibraryReflection.Optional(type, "Namespace", result.Unreadable, "LibraryType.Namespace"));
        result.Author = LibraryReflection.Display(LibraryReflection.Optional(type, "Author", result.Unreadable, "LibraryType.Author"));
        result.Comment = CommentText(LibraryReflection.Optional(type, "Comment", result.Unreadable, "LibraryType.Comment"));
        result.Unreadable = result.Unreadable.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>A comment value as text: a multilingual text by its items (<c>Items[].Text</c>), anything else by ToString.</summary>
    private static string? CommentText(object? comment)
    {
        if (comment is null || comment is string) return LibraryReflection.Display(comment);
        var items = LibraryReflection.Optional(comment, "Items", new List<string>(), "Comment.Items");
        if (items is null) return LibraryReflection.Display(comment);
        var texts = LibraryReflection.Enumerate(items, "comment items")
            .Select(item => LibraryReflection.Display(LibraryReflection.Optional(item, "Text", new List<string>(), "MultilingualTextItem.Text")))
            .Where(text => !string.IsNullOrEmpty(text)).Take(LibraryPolicy.MaxAttributes).ToArray();
        return texts.Length == 0 ? null : LibraryPolicy.Bound(string.Join(" | ", texts));
    }
}
