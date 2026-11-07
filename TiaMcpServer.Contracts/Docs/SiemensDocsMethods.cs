namespace TiaMcpServer.Contracts.Docs;

/// <summary>
/// Operation names of the in-host Siemens documentation reader (<c>siemens_docs</c>).
/// <c>OperationPolicyCatalog</c> is one flat namespace shared with the workers, so every method
/// carries the <c>docs_</c> prefix. All are <c>Observe</c>: they read public documentation pages
/// of the configured Siemens documentation hosts over HTTPS, never reach a worker, never touch
/// TIA Portal or a project and carry no project identity.
/// </summary>
public static class SiemensDocsMethods
{
    public const string Prefix = "docs_";

    public const string Search = "docs_search";
    public const string ReadPage = "docs_read_page";
    public const string GetToc = "docs_get_toc";
    public const string ListPublications = "docs_list_publications";

    /// <summary>Every method; all are classified <c>Observe</c>.</summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { Search, ReadPage, GetToc, ListPublications });

    /// <summary>Maps a public siemens_docs operation name to its method.</summary>
    public static string ForOperation(string operation) => Prefix + operation;

    public static bool IsDocsMethod(string? method)
        => method is not null && All.Contains(method, StringComparer.Ordinal);
}
