using TiaMcpServer.Safety.Pipeline;
using static TiaMcpServer.Hmi.HmiGuardDefinitions;

namespace TiaMcpServer.Hmi;

/// <summary>
/// The screen, script and batch rename writes in the HMI working state: their targets are resolved by the worker (no
/// planning read covers screens), so the effect lists the request, and an item depends on the latest earlier item on
/// the same screen (or, for rename_tags, on any of its tags).
/// </summary>
internal sealed partial class HmiWorkingState
{
    private HmiWriteEffect ResolveScreen(HmiWriteOperationRequest r, List<FiredGuard> guards)
    {
        var changes = new List<HmiFieldChange>();
        void Change(string field, string? current, string? requested) => changes.Add(new(field, current, requested, false));
        HmiTargetIdentity target;
        var dependencies = new List<HmiTargetIdentity>();
        switch (r.Operation)
        {
            case "create_screen":
            case "delete_screen":
                target = Identity("Screen", r.ScreenName!, group: r.GroupPath);
                if (r.Operation == "create_screen")
                {
                    Change("name", null, r.ScreenName);
                    if (r.Width is { } width) Change("Width", null, width.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    if (r.Height is { } height) Change("Height", null, height.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                else
                {
                    guards.Add(Fire(DeletesScreen, r, $"Deleting screen '{r.ScreenName}' removes it and its screen items; screen windows, navigation and scripts that name it are not changed."));
                }

                break;
            case "rename_screens":
            case "rename_tags":
                var kind = r.Operation == "rename_screens" ? "Screen" : "Tag";
                target = Identity(kind == "Screen" ? "ScreenBatch" : "TagBatch", string.Join(",", r.Renames!.Select(x => x.Name)));
                foreach (var rename in r.Renames!)
                {
                    Change($"name {rename.Name}", rename.Name, rename.NewName);
                    dependencies.Add(Identity(kind, rename.Name));
                }

                guards.Add(Fire(RenameUpdatesNoReferences, r, kind == "Screen"
                    ? "Renaming screens does not change screen windows, navigation or scripts that name the old names; check them with replace_in_scripts and set_screen_item_property."
                    : "Renaming HMI tags: whether TIA Portal follows the rename in tag dynamizations, scripts and alarm triggers is not verified (BetterTIA reports it open); check them with replace_in_scripts and set_dynamization_tag."));
                break;
            case "export_text_lists":
                target = Identity("TextLists", r.TextListNames is null ? "*" : string.Join(",", r.TextListNames));
                foreach (var name in r.TextListNames ?? new List<string> { "*" })
                    Change($"file {name}", null, $"{r.DirectoryPath!.Replace('/', '\\').TrimEnd('\\')}\\{(name == "*" ? "<name>" : name)}.xml");
                guards.Add(Fire(TextListFileUnverified, r, "export_text_lists calls HmiTextList.Export(FileInfo, ExportOptions), a member no Siemens document describes for WinCC Unified text lists; it is bound at runtime and fails api_member_missing when absent."
                    + (r.Overwrite == true ? " overwrite:true replaces existing files." : string.Empty)));
                break;
            case "import_text_lists":
                target = Identity("TextLists", "*");
                Change("import", null, r.FilePath);
                guards.Add(Fire(TextListFileUnverified, r, "import_text_lists calls HmiTextListComposition.Import(FileInfo, ImportOptions), a member no Siemens document describes for WinCC Unified text lists; it is bound at runtime and fails api_member_missing when absent. TIA Portal decides what the import creates"
                    + (r.Overwrite == true ? " or replaces (ImportOptions.Override)." : " (ImportOptions.None).")));
                break;
            case "replace_in_scripts":
                target = Identity("Scripts", r.ScreenName ?? "*", group: r.GroupPath);
                foreach (var pair in r.Replacements!) Change("script text", pair.Find, pair.Replace);
                break;
            default:
                target = Identity("ScreenItem", $"{r.ScreenName}/{r.ItemName ?? "(screen)"}", group: r.GroupPath);
                dependencies.Add(Identity("Screen", r.ScreenName!));
                switch (r.Operation)
                {
                    case "create_screen_item":
                        Change("itemType", null, r.ItemType);
                        foreach (var (field, value) in new[] { ("Left", r.Left), ("Top", r.Top), ("Width", r.Width), ("Height", r.Height) })
                            if (value is { } number) Change(field, null, number.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        break;
                    case "set_screen_item_property":
                        Change(r.Culture is null ? r.PropertyName! : $"{r.PropertyName}[{r.Culture}]", null, r.Value);
                        break;
                    case "set_dynamization_tag":
                        Change($"dynamization {r.PropertyName}", null, r.TagName);
                        break;
                }

                break;
        }

        dependencies.Add(target);
        return new(r.Operation, target, changes, null, Dependency(dependencies));
    }

    private void ApplyScreen(HmiWriteOperationRequest r, HmiWriteEffect effect)
    {
        Touch(effect.Target, r.OperationId);
        if (r.ScreenName is not null) Touch(Identity("Screen", r.ScreenName), r.OperationId);
        foreach (var rename in r.Renames ?? new())
        {
            var kind = r.Operation == "rename_screens" ? "Screen" : "Tag";
            Touch(Identity(kind, rename.Name), r.OperationId);
            Touch(Identity(kind, rename.NewName), r.OperationId);
        }
    }
}
