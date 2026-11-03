using Siemens.Engineering;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Units;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Libraries;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Plc;

/// <summary>
/// read_tag_comments: the multilingual comment of every tag (<c>PlcTag.Comment</c>, a <c>MultilingualText</c>;
/// tags-types.md V21 API list) and user constant (<c>PlcUserConstant.Comment</c>, listed without a type, so it is
/// read by exact name at runtime and accepted only as a <c>MultilingualText</c>) of the tag tables of every PLC
/// <c>plcName</c> selects and of their software units (<c>unit.TagTableGroup</c>, software-units.md; each table
/// carries its <c>unitName</c>), with the table and folder filters of <c>list_tag_tables</c>. Each item of
/// <c>MultilingualText.Items</c> with a non-empty <c>Text</c> becomes one entry (its <c>Language.Culture</c> name;
/// an unreadable culture gets the distinct key <c>(unreadable language #n)</c>, n the item's position, and a
/// message). Nothing unreadable is dropped silently: it yields a message, a null comment and <c>complete:false</c>.
/// </summary>
internal static class PlcTagCommentService
{
    public const int MaxMessages = 50;

    public static PlcTagCommentInventoryInfo Read(ProjectBase project, PlcSourceFolderRequestInfo request)
    {
        var inventory = new PlcTagCommentInventoryInfo();
        var folder = request.FolderPath is null ? null : Normalize(request.FolderPath);
        foreach (var discovered in PlcSoftwareLocator.FindEveryPlc(project, request.PlcName, inventory.Messages))
        {
            var plc = new PlcTagCommentPlcInfo { PlcName = discovered.Software.Name ?? string.Empty, DeviceName = discovered.DeviceName ?? string.Empty };
            try
            {
                Walk(discovered.Software.TagTableGroup, "/", null, request.TableName, folder, plc.Tables, inventory.Messages);
            }
            catch (EngineeringException ex) when (Infrastructure.OpennessExceptions.IsRecoverable(ex))
            {
                inventory.Messages.Add($"The tag tables of PLC '{plc.PlcName}' could not be read: {ex.Message}");
            }

            try
            {
                if (discovered.Software.GetService<PlcUnitProvider>() is { } provider)
                    Each(provider.UnitGroup.Units, $"a software unit of PLC '{plc.PlcName}'", inventory.Messages, unit =>
                        Walk(unit.TagTableGroup, "/", unit.Name, request.TableName, folder, plc.Tables, inventory.Messages));
            }
            catch (EngineeringException ex) when (Infrastructure.OpennessExceptions.IsRecoverable(ex))
            {
                inventory.Messages.Add($"The software units of PLC '{plc.PlcName}' could not be read: {ex.Message}");
            }

            inventory.Plcs.Add(plc);
        }

        if (inventory.Plcs.Count == 0 && inventory.Messages.Count == 0)
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                $"No PLC software{(request.PlcName is null ? string.Empty : $" named '{request.PlcName}'")} was found in the project.");
        if ((request.TableName is not null || folder is not null) && inventory.Messages.Count == 0 && inventory.Plcs.All(p => p.Tables.Count == 0))
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                $"No tag table matched{(request.TableName is null ? string.Empty : $" tableName '{request.TableName}'")}{(folder is null ? string.Empty : $" folderPath '{folder}'")}.");
        inventory.Complete = inventory.Messages.Count == 0;
        if (inventory.Messages.Count > MaxMessages)
        {
            var omitted = inventory.Messages.Count - MaxMessages;
            inventory.Messages.RemoveRange(MaxMessages, omitted);
            inventory.Messages.Add($"... and {omitted} more messages.");
        }

        return inventory;
    }

    private static void Walk(PlcTagTableGroup group, string folderPath, string? unitName, string? tableName, string? folder, List<PlcTagTableCommentsInfo> tables, List<string> messages)
    {
        Each(group.TagTables, $"a tag table in tag table group '{folderPath}'", messages, table =>
        {
            var name = table.Name;
            if (folder is not null && !string.Equals(folder, folderPath, StringComparison.OrdinalIgnoreCase)) return;
            if (tableName is not null && !string.Equals(tableName, name, StringComparison.OrdinalIgnoreCase)) return;
            var info = new PlcTagTableCommentsInfo { UnitName = unitName, FolderPath = folderPath, TableName = name };
            Each(table.Tags, $"a tag in tag table '{name}'", messages, tag =>
                info.Members.Add(new PlcMemberCommentInfo { Kind = "Tag", Name = tag.Name, Comment = Texts(tag.Comment, $"tag '{tag.Name}' in tag table '{name}'", messages) }));
            Each(table.UserConstants, $"a user constant in tag table '{name}'", messages, constant =>
                info.Members.Add(new PlcMemberCommentInfo { Kind = "UserConstant", Name = constant.Name, Comment = ConstantComment(constant, name, messages) }));
            tables.Add(info);
        });
        Each(group.Groups, $"a nested group in tag table group '{folderPath}'", messages, child =>
            Walk(child, folderPath == "/" ? "/" + child.Name : $"{folderPath}/{child.Name}", unitName, tableName, folder, tables, messages));
    }

    /// <summary>PlcUserConstant.Comment is listed without a type: read by exact name, accepted only as MultilingualText.</summary>
    private static List<PlcCommentTextInfo>? ConstantComment(PlcUserConstant constant, string table, List<string> messages)
    {
        var where = $"user constant '{constant.Name}' in tag table '{table}'";
        object? value;
        try
        {
            value = LibraryReflection.Required(constant, "Comment", "PlcUserConstant.Comment (tags-types.md, V21 API list)");
        }
        catch (WorkerOperationException ex)
        {
            messages.Add($"The comment of {where} could not be read: {ex.Message}");
            return null;
        }

        if (value is MultilingualText text) return Texts(text, where, messages);
        messages.Add($"The comment of {where} is a {(value is null ? "null value" : value.GetType().FullName)}, not a MultilingualText; it is not reported.");
        return null;
    }

    private static List<PlcCommentTextInfo>? Texts(MultilingualText comment, string where, List<string> messages)
    {
        try
        {
            var entries = new List<PlcCommentTextInfo>();
            var position = 0;
            foreach (MultilingualTextItem item in comment.Items)
            {
                position++;
                var text = item.Text;
                if (string.IsNullOrEmpty(text)) continue;
                entries.Add(new PlcCommentTextInfo { Culture = Culture(item.Language, position, where, messages), Text = text });
            }

            return entries;
        }
        catch (EngineeringException ex) when (Infrastructure.OpennessExceptions.IsRecoverable(ex))
        {
            messages.Add($"The comment of {where} could not be read: {ex.Message}");
            return null;
        }
    }

    /// <summary>The item's culture name; an unreadable one gets a distinct key by item position (never one shared "unknown") and a message.</summary>
    private static string Culture(Language language, int position, string where, List<string> messages)
    {
        string? name;
        string? reason = null;
        try
        {
            name = language.Culture?.Name;
        }
        catch (EngineeringException ex) when (Infrastructure.OpennessExceptions.IsRecoverable(ex))
        {
            name = null;
            reason = ex.Message;
        }

        if (!string.IsNullOrEmpty(name)) return name!;
        messages.Add($"The language of comment item {position} of {where} could not be read{(reason is null ? string.Empty : $" ({reason})")}; its text is reported under '(unreadable language #{position})'.");
        return $"(unreadable language #{position})";
    }

    private static string Normalize(string folderPath)
    {
        var segments = folderPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
        return "/" + string.Join("/", segments);
    }

    private static void Each<T>(IEnumerable<T> items, string what, List<string> messages, Action<T> read)
    {
        try
        {
            foreach (var item in items)
            {
                try
                {
                    read(item);
                }
                catch (EngineeringException ex) when (Infrastructure.OpennessExceptions.IsRecoverable(ex))
                {
                    messages.Add($"{what} could not be read: {ex.Message}");
                }
            }
        }
        catch (EngineeringException ex) when (Infrastructure.OpennessExceptions.IsRecoverable(ex))
        {
            messages.Add($"{what} could not be enumerated: {ex.Message}");
        }
    }
}
