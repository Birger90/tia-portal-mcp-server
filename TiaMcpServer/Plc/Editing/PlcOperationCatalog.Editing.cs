using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Interface;

namespace TiaMcpServer.Plc;

/// <summary>
/// The operations adopted from the BetterTIA tools (watch, writer): DB snapshots, network texts, member and batch renames,
/// tag comment replacement. Their field sets and shape rules; nothing here reads the project.
/// </summary>
public static partial class PlcOperationCatalog
{
    // Non-string required fields (networkNumber, replacements) are listed as optional in the specs, because the shared
    // required check reads string fields only, and are required by ValidateEditingItem.
    private static IReadOnlyDictionary<string, Spec> WithEditingSpecs(IReadOnlyDictionary<string, Spec> existing)
    {
        var specs = new Dictionary<string, Spec>(existing, StringComparer.Ordinal);
        specs[PlcBlockEditMethods.GetDbSnapshot] = Read(new[] { "blockPath" }, new[] { "memberPath", "maxElements" });
        specs[PlcBlockEditMethods.CreateDbSnapshot] = Write(new[] { "blockPath" }, new[] { "memberPath", "maxElements" });
        specs[PlcBlockEditMethods.SetNetworkText] = Write(new[] { "blockPath" }, new[] { "networkNumber", "title", "comment", "culture", "expectedContentHash" });
        specs[PlcBlockEditMethods.ReplaceNetworkText] = Write(new[] { "blockPath" }, new[] { "replacements", "culture", "textScope", "expectedContentHash" });
        specs[PlcBlockEditMethods.RenameTags] = Write(Array.Empty<string>(), new[] { "plcName", "renames" });
        specs[PlcBlockEditMethods.RenameObjects] = Write(Array.Empty<string>(), new[] { "plcName", "renames" });
        specs[PlcBlockEditMethods.ReplaceTagComments] = Write(Array.Empty<string>(), new[] { "plcName", "tableName", "folderPath", "replacements", "culture" });
        specs[PlcBlockEditMethods.RenameMembers] = Write(Array.Empty<string>(), new[] { "blockPath", "typePath", "memberRenames", "expectedContentHash" });
        specs[PlcBlockEditMethods.ExportTagTable] = Write(new[] { "tableName", "filePath" }, new[] { "plcName", "folderPath", "overwrite" });
        specs[PlcBlockEditMethods.ImportTagTable] = Write(new[] { "filePath" }, new[] { "plcName", "tableName", "folderPath", "overwrite" });
        specs[PlcBlockEditMethods.ExportOpcUaXml] = Write(new[] { "filePath" }, new[] { "plcName", "overwrite" });
        return specs;
    }

    /// <summary>The string fields of the adopted operations (comment and blockPath are shared with older operations).</summary>
    private static IEnumerable<(string Name, Func<PlcOperationRequest, string?> Get)> EditingStringFields() => new (string, Func<PlcOperationRequest, string?>)[]
    {
        ("title", op => op.Title),
        ("culture", op => op.Culture),
        ("textScope", op => op.TextScope),
    };

    private static IEnumerable<(string Name, Func<PlcOperationRequest, bool> IsSet)> EditingOtherFields() => new (string, Func<PlcOperationRequest, bool>)[]
    {
        ("networkNumber", op => op.NetworkNumber is not null),
        ("replacements", op => op.Replacements is not null),
        ("memberRenames", op => op.MemberRenames is not null),
        ("renames", op => op.Renames is not null),
    };

    public const int MaxReplacements = 100;

    private static void ValidateEditingItem(PlcOperationRequest op, string prefix, List<string> errors)
    {
        void Error(string message) => errors.Add($"{prefix}: {message}");
        if (op.Culture is not null && string.IsNullOrWhiteSpace(op.Culture)) Error("culture must be nonblank when supplied.");
        switch (op.Operation)
        {
            case PlcBlockEditMethods.GetDbSnapshot:
            case PlcBlockEditMethods.CreateDbSnapshot:
                if (op.MaxElements is < 1 or > PlcDbSnapshotDocument.MaxValues)
                    Error($"maxElements must be between 1 and {PlcDbSnapshotDocument.MaxValues} (default {PlcDbSnapshotDocument.DefaultMaxValues}).");
                if (op.MemberPath is not null && string.IsNullOrWhiteSpace(op.MemberPath))
                    Error("memberPath must be nonblank when supplied.");
                break;
            case PlcBlockEditMethods.ExportOpcUaXml:
                if (op.FilePath is not null && Contracts.Plc.TechnologyObjects.TechnologyObjectPolicy.ValidateLocalFilePath(op.FilePath) is { } opcUaRefusal)
                    Error("'filePath' " + opcUaRefusal);
                break;
            case PlcBlockEditMethods.ExportTagTable:
            case PlcBlockEditMethods.ImportTagTable:
                if (op.FilePath is not null && Contracts.Plc.TechnologyObjects.TechnologyObjectPolicy.ValidateLocalFilePath(op.FilePath) is { } refusal)
                    Error("'filePath' " + refusal);
                if (op.TableName is not null && string.IsNullOrWhiteSpace(op.TableName)) Error("tableName must be nonblank when supplied.");
                break;
            case PlcBlockEditMethods.SetNetworkText:
                if (op.NetworkNumber is null) Error("networkNumber is required (1-based).");
                else if (op.NetworkNumber < 1) Error("networkNumber must be 1 or more.");
                if (op.Title is null && op.Comment is null) Error("set_network_text needs title, comment or both.");
                break;
            case PlcBlockEditMethods.ReplaceTagComments:
                if (op.Replacements is null) Error($"replacements is required: 1 to {MaxReplacements} {{ find, replace }} pairs.");
                ValidateReplacements(op.Replacements, Error);
                if (op.FolderPath is not null && op.TableName is null) Error("folderPath is only valid with tableName.");
                break;
            case PlcBlockEditMethods.ReplaceNetworkText:
                if (op.Replacements is null) Error($"replacements is required: 1 to {MaxReplacements} {{ find, replace }} pairs.");
                ValidateReplacements(op.Replacements, Error);
                if (op.TextScope is not null && !SimaticMlNetworkTexts.Scopes.Contains(op.TextScope, StringComparer.Ordinal))
                    Error($"textScope must be one of {string.Join(", ", SimaticMlNetworkTexts.Scopes)}.");
                break;
            case PlcBlockEditMethods.RenameTags:
            case PlcBlockEditMethods.RenameObjects:
                if (op.Renames is null || op.Renames.Count is 0 or > PlcBatchRenamePlan.MaxRenames)
                {
                    Error($"renames is required: 1 to {PlcBatchRenamePlan.MaxRenames} {{ name, newName }} entries.");
                    break;
                }

                if (op.Renames.Any(r => r is null || string.IsNullOrWhiteSpace(r.Name) || string.IsNullOrWhiteSpace(r.NewName)))
                {
                    Error("every rename needs a nonblank name and newName.");
                    break;
                }

                foreach (var rename in op.Renames.Where(r => r.NewName.Trim().Length != r.NewName.Length || r.NewName.Any(char.IsControl)
                             || op.Operation == PlcBlockEditMethods.RenameObjects && r.NewName.IndexOfAny(new[] { '/', '\\', '"' }) >= 0))
                    Error($"newName '{rename.NewName}' must not have surrounding blanks or control characters{(op.Operation == PlcBlockEditMethods.RenameObjects ? ", '/', '\\' or '\"'" : string.Empty)}.");
                if (op.Renames.Any(r => r.NewName.StartsWith(PlcBatchRenamePlan.TemporaryPrefix, StringComparison.OrdinalIgnoreCase)))
                    Error($"newName must not start with '{PlcBatchRenamePlan.TemporaryPrefix}', the prefix of the temporary names.");
                try
                {
                    _ = PlcBatchRenamePlan.Plan(op.Renames.Select(r => r.Name).ToList(), op.Renames.Select(r => r.NewName).ToList(), "check");
                }
                catch (ArgumentException exception)
                {
                    Error(exception.Message);
                }

                break;
            case PlcBlockEditMethods.RenameMembers:
                if (string.IsNullOrWhiteSpace(op.BlockPath) == string.IsNullOrWhiteSpace(op.TypePath))
                    Error("give exactly one of blockPath (a block) or typePath (a PLC data type).");
                if (op.MemberRenames is null || op.MemberRenames.Count is 0 or > SimaticMlMemberRenames.MaxRenames)
                    Error($"memberRenames is required: 1 to {SimaticMlMemberRenames.MaxRenames} {{ memberPath, newName, section? }} entries.");
                else
                    foreach (var rename in op.MemberRenames)
                    {
                        if (rename is null) { Error("memberRenames must not contain null."); continue; }
                        PlcBlockEditValidation.MemberPath(rename.MemberPath ?? string.Empty, allowIndices: false, prefix, errors);
                        if (string.IsNullOrWhiteSpace(rename.NewName) || rename.NewName.Trim().Length != rename.NewName.Length
                            || rename.NewName.IndexOfAny(new[] { '"', '.', '[', ']' }) >= 0 || rename.NewName.Any(char.IsControl))
                            Error($"newName '{rename.NewName}' must be nonblank, without surrounding blanks, control characters, '\"', '.', '[' or ']'.");
                        if (rename.Section is not null && op.TypePath is not null && !string.Equals(rename.Section, "None", StringComparison.OrdinalIgnoreCase))
                            Error("a PLC data type has only the section None; omit section.");
                    }

                break;
        }
    }

    private static void ValidateReplacements(List<PlcTextReplacementInfo>? replacements, Action<string> error)
    {
        if (replacements is null) return;
        if (replacements.Count is 0 or > MaxReplacements)
        {
            error($"replacements must hold 1 to {MaxReplacements} {{ find, replace }} pairs.");
            return;
        }

        if (replacements.Any(r => r is null)) error("replacements must not contain null.");
        else
            try
            {
                _ = SimaticMlNetworkTexts.Map(replacements);
            }
            catch (ArgumentException exception)
            {
                error(exception.Message);
            }
    }
}
