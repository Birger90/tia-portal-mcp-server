using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Interface;

namespace TiaMcpServer.Hmi;

/// <summary>Shape rules of the WinCC Unified screen, script and batch rename writes (adopted from BetterTIA/writer).</summary>
public static partial class HmiWriteCatalog
{
    public static readonly IReadOnlySet<string> ScreenOperations = new HashSet<string>(StringComparer.Ordinal)
    {
        "create_screen", "delete_screen", "rename_screens", "create_screen_item", "delete_screen_item",
        "set_screen_item_property", "set_dynamization_tag", "replace_in_scripts", "rename_tags",
        "export_text_lists", "import_text_lists",
    };

    public const int MaxTextListNames = 200;

    public const int MaxScriptReplacements = 100;

    private static void ValidateScreenItem(HmiWriteOperationRequest r, Action<string> error)
    {
        if (!ScreenOperations.Contains(r.Operation)) return;
        foreach (var (name, value) in new[] { ("screenName", r.ScreenName), ("itemName", r.ItemName), ("propertyName", r.PropertyName) })
            if (value is not null && !ValidName(value)) error($"'{name}' is invalid.");
        if (r.ItemType is not null && (r.ItemType.Length == 0 || !r.ItemType.All(c => char.IsLetterOrDigit(c) || c == '_')))
            error("'itemType' must be the Openness type name of a screen item, e.g. HmiButton, HmiIOField, HmiText, HmiRectangle.");
        foreach (var (name, value) in new[] { ("width", r.Width), ("height", r.Height) })
            if (value is < 0) error($"'{name}' must not be negative.");
        if (r.Operation is "rename_screens" or "rename_tags")
        {
            if (r.Renames is null || r.Renames.Count is 0 or > PlcBatchRenamePlan.MaxRenames)
            {
                error($"'renames' must hold 1 to {PlcBatchRenamePlan.MaxRenames} {{ name, newName }} entries.");
                return;
            }

            if (r.Renames.Any(x => x is null || !ValidName(x.Name ?? string.Empty) || !ValidName(x.NewName ?? string.Empty)))
                error("Every rename needs a valid name and newName.");
            else if (r.Renames.Any(x => x.NewName.StartsWith(PlcBatchRenamePlan.TemporaryPrefix, StringComparison.OrdinalIgnoreCase)))
                error($"'newName' must not start with '{PlcBatchRenamePlan.TemporaryPrefix}', the prefix of the temporary names.");
            else
                try
                {
                    _ = PlcBatchRenamePlan.Plan(r.Renames.Select(x => x.Name).ToList(), r.Renames.Select(x => x.NewName).ToList(), "check");
                }
                catch (ArgumentException exception)
                {
                    error(exception.Message);
                }
        }

        if (r.Operation == "replace_in_scripts")
        {
            if (r.Replacements is null || r.Replacements.Count is 0 or > MaxScriptReplacements || r.Replacements.Any(x => x is null))
                error($"'replacements' must hold 1 to {MaxScriptReplacements} {{ find, replace }} pairs.");
            else
                try
                {
                    _ = SimaticMlNetworkTexts.Map(r.Replacements);
                }
                catch (ArgumentException exception)
                {
                    error(exception.Message);
                }

            if (r.GroupPath is not null && r.ScreenName is null) error("'groupPath' is only valid with screenName.");
        }

        if (r.Culture is not null && !ValidCulture(r.Culture)) error("'culture' must be a culture name, e.g. en-US.");
        foreach (var (name, path) in new[] { ("directoryPath", r.DirectoryPath), ("filePath", r.FilePath) })
            if (path is not null && Contracts.Plc.TechnologyObjects.TechnologyObjectPolicy.ValidateLocalFilePath(path) is { } refusal) error($"'{name}' {refusal}");
        if (r.TextListNames is { } lists && (lists.Count is 0 or > MaxTextListNames || lists.Any(n => n is null || !ValidName(n))
                || lists.Distinct(StringComparer.OrdinalIgnoreCase).Count() != lists.Count))
            error($"'textListNames' must hold 1 to {MaxTextListNames} distinct valid text list names.");
    }
}
