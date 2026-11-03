namespace TiaMcpServer.Contracts.Plc;

/// <summary>
/// Siemens-free rules of the tag comment that create_tag and update_tag write (<c>comment</c> with <c>commentCulture</c>,
/// K22). <c>PlcTag.Comment</c> is a <c>MultilingualText</c>; the Siemens skill (tia-project-general, language settings
/// §5) writes one by finding the <c>Language</c> in <c>LanguageSettings.Languages</c>, then
/// <c>MultilingualText.Items.Find(language)</c>, and setting <c>MultilingualTextItem.Text</c>. No member that creates an
/// item is documented, so a culture TIA Portal has no item for is reported, not created. A culture that is not an active
/// project language is passed on with a warning (TIA Portal decides, as for the alarm text exports); one that is not a
/// TIA Portal language at all is refused, because there is nothing to pass. Shared by the worker and the FakeWorker.
/// </summary>
public static class PlcTagCommentRules
{
    /// <summary>
    /// The catalogue culture that <paramref name="requested"/> names (case-insensitive), with a warning when it is not
    /// active; or the refusal when the catalogue has no such language or more than one.
    /// </summary>
    public static (string? Culture, string? Warning, string? Refusal) Resolve(string requested, IReadOnlyList<string> catalogue, IReadOnlyList<string> active)
    {
        var matches = catalogue.Where(c => string.Equals(c, requested, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (matches.Count != 1)
            return (null, null, $"commentCulture '{requested}' is not a TIA Portal language (LanguageSettings.Languages); the active project languages are "
                + $"{Shown(active)}. Nothing was changed.");
        var warning = active.Contains(matches[0], StringComparer.OrdinalIgnoreCase) ? null
            : $"commentCulture '{matches[0]}' is not an active project language (active: {Shown(active)}); TIA Portal decides whether the tag comment takes a text in it.";
        return (matches[0], warning, null);
    }

    /// <summary>TIA Portal's <c>Items.Find(language)</c> returned no item: there is no text to set in that culture.</summary>
    public static string NoItem(string tagName, string culture, IReadOnlyList<string> items)
        => $"TIA Portal has no comment text item for culture '{culture}' on tag '{tagName}' (MultilingualText.Items.Find returned null; items: {Shown(items)}). "
            + "Activate the language in the project first.";

    /// <summary>The comment read back after writing differs from the request.</summary>
    public static string Mismatch(string tagName, string culture, string requested, string? actual)
        => $"The comment of tag '{tagName}' reads back {(actual is null ? "no text" : $"'{actual}'")} in '{culture}' after writing '{requested}'.";

    private static string Shown(IReadOnlyList<string> cultures) => cultures.Count == 0 ? "(none)" : string.Join(", ", cultures);
}
