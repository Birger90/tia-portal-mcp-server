namespace TiaMcpServer.Contracts.Library;

/// <summary>
/// K80 (live 2026-11-03): <c>open_global_library</c> with the path of a system library (PrintingSysLib.as21, as
/// list_global_library_infos returns it) was refused by TIA Portal: "System and Corporate global libraries cannot be opened with a
/// path." V21 documents <c>GlobalLibraryComposition.Open(GlobalLibraryInfo)</c> ("The global library info associated with a global
/// library to be opened") next to <c>Open(FileInfo, OpenMode)</c>, and <c>GlobalLibraryInfo.LibraryType</c>/<c>.Path</c>/<c>.Name</c>.
/// The Siemens-free decision, shared by the worker and the FakeWorker: the caller still names the library by its path; when
/// <c>GetGlobalLibraryInfos()</c> lists that path as a System or Corporate library, the worker opens it with
/// <c>Open(GlobalLibraryInfo)</c>, which takes no open mode (TIA Portal opens such libraries read-only). Not live-tested.
/// </summary>
public static class GlobalLibraryInfoOpen
{
    public const string Call = "Open(GlobalLibraryInfo)";

    public const string Documented = "GlobalLibraryComposition.Open(GlobalLibraryInfo)";

    /// <summary>True for the <c>GlobalLibraryInfo.LibraryType</c> names TIA Portal refuses to open by path.</summary>
    public static bool OpensByInfo(string? libraryType)
        => string.Equals(libraryType, "System", StringComparison.OrdinalIgnoreCase)
            || string.Equals(libraryType, "Corporate", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The single info whose path equals <paramref name="path"/> and whose type opens by info; null when none does (the path route
    /// stays, TIA Portal decides). Several matching infos are ambiguous.
    /// </summary>
    public static (T Info, string LibraryType)? Find<T>(IEnumerable<T> infos, string path, Func<T, string?> readPath, Func<T, string?> readType,
        out string? ambiguity)
    {
        ambiguity = null;
        var matches = infos.Select(info => (Info: info, Path: readPath(info), Type: readType(info)))
            .Where(entry => LibraryPolicy.SamePath(entry.Path, path) && OpensByInfo(entry.Type)).ToList();
        if (matches.Count == 0) return null;
        if (matches.Count > 1)
        {
            ambiguity = $"GetGlobalLibraryInfos() lists {matches.Count} system or corporate libraries with the path '{path}'; none is chosen.";
            return null;
        }

        return (matches[0].Info, matches[0].Type!);
    }

    /// <summary>The refusal of options Open(GlobalLibraryInfo) cannot honour (it takes no OpenMode and does not upgrade); null when none.</summary>
    public static string? OptionRefusal(LibraryRequestInfo request, string libraryType)
        => request.Upgrade
            ? $"path names a {libraryType} global library, which TIA Portal opens only by its GlobalLibraryInfo ({Documented}); that call does not upgrade, so upgrade:true is not possible. Nothing was opened."
            : string.Equals(request.OpenMode, "ReadWrite", StringComparison.Ordinal)
                ? $"path names a {libraryType} global library, which TIA Portal opens only by its GlobalLibraryInfo ({Documented}); that call takes no OpenMode and TIA Portal opens such libraries read-only, so openMode ReadWrite is not possible. Omit openMode or use ReadOnly. Nothing was opened."
                : null;

    public static string Consequence(string libraryType)
        => $"The path is a {libraryType} global library: it is opened with {Documented} (TIA Portal refuses a path for system and corporate libraries); "
            + "the call takes no OpenMode, TIA Portal opens it read-only.";
}
