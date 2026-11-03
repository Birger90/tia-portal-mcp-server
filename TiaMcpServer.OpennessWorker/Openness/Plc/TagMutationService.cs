using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Tags;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Plc;

public static class TagMutationService
{
    public static TagMutationResultInfo CreateTagTable(
        ProjectBase project,
        string? plcName,
        string tableName,
        string? folderPath)
    {
        RequireName(tableName, "TableName");

        var (plc, group) = ResolveGroup(project, plcName, folderPath);

        var table = group.TagTables.Create(tableName);
        return Result("create_tag_table", project, plc.Name, table.Name, NormalizeFolderPath(folderPath), null, null);
    }

    public static TagMutationResultInfo DeleteTagTable(
        ProjectBase project,
        string? plcName,
        string tableName,
        string? folderPath)
    {
        var (plc, table) = ResolveTable(project, plcName, tableName, folderPath);
        table.Delete();
        return Result("delete_tag_table", project, plc.Name, tableName, NormalizeFolderPath(folderPath), null, null);
    }

    public static TagMutationResultInfo CreateTag(
        ProjectBase project,
        string? plcName,
        string tableName,
        string? folderPath,
        string name,
        string dataType,
        string? logicalAddress,
        string? comment = null,
        string? commentCulture = null)
    {
        RequireName(name, "Name");
        RequireName(dataType, "DataType");

        var (plc, table) = ResolveTable(project, plcName, tableName, folderPath);
        // The comment's language is resolved before the tag exists, so a culture TIA Portal does not know changes nothing.
        var language = CommentLanguage(project, comment, commentCulture);

        var tag = table.Tags.Create(name, dataType, logicalAddress ?? string.Empty);
        var result = Result("create_tag", project, plc.Name, table.Name, NormalizeFolderPath(folderPath), tag.Name, null);
        if (language is null) return result;
        try
        {
            return WithComment(result, WriteComment(tag, language.Value, comment!));
        }
        catch (Exception exception) when (exception is WorkerOperationException or EngineeringException)
        {
            // The tag was created for this comment: remove it again so a failed item leaves nothing behind, and say what happened.
            var cleanup = TryDeleteCreated(tag);
            if (exception is WorkerOperationException failed)
                throw new WorkerOperationException(failed.FailureCategory, $"create_tag: {failed.Message} {cleanup}", engineeringError: failed.EngineeringError);
            var engineering = (EngineeringException)exception;
            throw new WorkerOperationException(WorkerExecution.Classify(engineering, mutates: true),
                $"create_tag: setting the {language.Value.Culture} comment failed: {engineering.Message} {cleanup}", engineeringError: WorkerExecution.Describe(engineering));
        }
    }

    public static TagMutationResultInfo UpdateTag(
        ProjectBase project,
        string? plcName,
        string tableName,
        string? folderPath,
        string name,
        string? newName,
        string? dataType,
        string? logicalAddress,
        bool? externalAccessible,
        bool? externalVisible,
        bool? externalWritable,
        string? comment = null,
        string? commentCulture = null)
    {
        RequireName(name, "Name");

        var resolved = TagTargetResolver.Resolve(project, plcName, tableName, folderPath, name);
        var language = CommentLanguage(project, comment, commentCulture);
        var table = resolved.Table;
        var tag = resolved.Tag;

        // Rename first and read it back: Portal silently keeps the old name when another object holds
        // the new one, so a call that did not throw is no proof. Fail before any other property changes.
        if (!string.IsNullOrWhiteSpace(newName))
        {
            tag.Name = newName!;
            var actualName = tag.Name;
            if (!string.Equals(actualName, newName, StringComparison.Ordinal))
            {
                throw new WorkerOperationException(
                    WorkerFailureCategories.PostconditionFailed,
                    $"update_tag: Portal did not rename tag '{name}' to '{newName}'; the tag is still named '{actualName}'. "
                    + "Another object may already hold that name. No other property was changed.");
            }
        }

        if (!string.IsNullOrWhiteSpace(dataType))
        {
            tag.DataTypeName = dataType!;
        }

        if (logicalAddress is not null)
        {
            tag.LogicalAddress = logicalAddress;
        }

        if (externalAccessible.HasValue)
        {
            tag.ExternalAccessible = externalAccessible.Value;
        }

        if (externalVisible.HasValue)
        {
            tag.ExternalVisible = externalVisible.Value;
        }

        if (externalWritable.HasValue)
        {
            tag.ExternalWritable = externalWritable.Value;
        }

        var result = Result("update_tag", project, resolved.PlcName, table.Name, resolved.FolderPath, tag.Name, null);
        // Last, after every other property: a refused comment leaves the earlier changes applied, which the failure says.
        if (language is null) return result;
        try
        {
            return WithComment(result, WriteComment(tag, language.Value, comment!));
        }
        catch (WorkerOperationException failed)
        {
            throw new WorkerOperationException(failed.FailureCategory, $"update_tag: {failed.Message} {EarlierChanges}", engineeringError: failed.EngineeringError);
        }
        catch (EngineeringException engineering)
        {
            throw new WorkerOperationException(WorkerExecution.Classify(engineering, mutates: true),
                $"update_tag: setting the {language.Value.Culture} comment failed: {engineering.Message} {EarlierChanges}", engineeringError: WorkerExecution.Describe(engineering));
        }
    }

    private const string EarlierChanges = "Any other property this item changes (rename, data type, address, flags) was applied before the comment and stays.";

    /// <summary>
    /// The language of a tag comment write (K22): null when the item sets no comment; otherwise the one language of
    /// <c>LanguageSettings.Languages</c> whose culture is <paramref name="culture"/> (<see cref="PlcTagCommentRules.Resolve"/>,
    /// the Siemens skill's lookup). A culture that is not active is passed on with a warning (TIA Portal decides); one TIA
    /// Portal does not know is refused before anything changes.
    /// </summary>
    private static (Language Language, string Culture)? CommentLanguage(ProjectBase project, string? comment, string? culture)
    {
        if (comment is null && culture is null) return null;
        if (comment is null || string.IsNullOrWhiteSpace(culture))
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "comment and commentCulture are given together. Nothing was changed.");
        var catalogue = project.LanguageSettings.Languages.ToList();
        var active = project.LanguageSettings.ActiveLanguages.Select(CultureName).ToList();
        var (resolved, warning, refusal) = PlcTagCommentRules.Resolve(culture!, catalogue.Select(CultureName).ToList(), active);
        if (refusal is not null) throw new WorkerOperationException(WorkerFailureCategories.ValidationError, refusal);
        if (warning is not null) Console.Error.WriteLine(warning);
        return (catalogue.First(language => string.Equals(CultureName(language), resolved, StringComparison.OrdinalIgnoreCase)), resolved!);
    }

    /// <summary>
    /// Sets the comment text of <paramref name="language"/> through <c>MultilingualText.Items.Find(Language)</c> and
    /// <c>MultilingualTextItem.Text</c> and reads it back; TIA Portal's own refusal (an exception from the setter) propagates.
    /// </summary>
    private static (string Culture, string Text) WriteComment(PlcTag tag, (Language Language, string Culture) language, string text)
    {
        var item = tag.Comment.Items.Find(language.Language)
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                PlcTagCommentRules.NoItem(tag.Name, language.Culture, tag.Comment.Items.Select(i => CultureName(i.Language)).ToList()));
        item.Text = text;
        var actual = tag.Comment.Items.Find(language.Language)?.Text;
        if (!string.Equals(actual ?? string.Empty, text, StringComparison.Ordinal))
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, PlcTagCommentRules.Mismatch(tag.Name, language.Culture, text, actual));
        return (language.Culture, actual ?? string.Empty);
    }

    private static TagMutationResultInfo WithComment(TagMutationResultInfo result, (string Culture, string Text) comment)
    {
        result.CommentCulture = comment.Culture;
        result.Comment = comment.Text;
        return result;
    }

    private static string TryDeleteCreated(PlcTag tag)
    {
        try
        {
            tag.Delete();
            return "The tag created for it was deleted again.";
        }
        catch (EngineeringException exception)
        {
            return $"The tag was created, but deleting it again failed ({exception.Message}); remove it with delete_tag.";
        }
    }

    public static TagMutationResultInfo DeleteTag(
        ProjectBase project,
        string? plcName,
        string tableName,
        string? folderPath,
        string name)
    {
        RequireName(name, "Name");

        var (plc, table) = ResolveTable(project, plcName, tableName, folderPath);
        var tag = table.Tags.Find(name) ??
            throw NotFound($"Tag '{name}' was not found in tag table '{tableName}'.");

        tag.Delete();
        return Result("delete_tag", project, plc.Name, table.Name, NormalizeFolderPath(folderPath), name, null);
    }

    public static TagMutationResultInfo CreateUserConstant(
        ProjectBase project,
        string? plcName,
        string tableName,
        string? folderPath,
        string name,
        string dataType,
        string value)
    {
        RequireName(name, "Name");
        RequireName(dataType, "DataType");

        var (plc, table) = ResolveTable(project, plcName, tableName, folderPath);

        var constant = table.UserConstants.Create(name);
        constant.DataTypeName = dataType;
        constant.Value = value;

        return Result("create_user_constant", project, plc.Name, table.Name, NormalizeFolderPath(folderPath), null, constant.Name);
    }

    public static TagMutationResultInfo UpdateUserConstant(
        ProjectBase project,
        string? plcName,
        string tableName,
        string? folderPath,
        string name,
        string? dataType,
        string? value)
    {
        RequireName(name, "Name");

        var (plc, table) = ResolveTable(project, plcName, tableName, folderPath);
        var constant = table.UserConstants.Find(name) ??
            throw NotFound($"User constant '{name}' was not found in tag table '{tableName}'.");
        PlcWritePreconditions.RequireReadableValue(constant);

        if (!string.IsNullOrWhiteSpace(dataType))
        {
            constant.DataTypeName = dataType!;
        }

        if (value is not null)
        {
            constant.Value = value;
        }

        return Result("update_user_constant", project, plc.Name, table.Name, NormalizeFolderPath(folderPath), null, constant.Name);
    }

    public static TagMutationResultInfo DeleteUserConstant(
        ProjectBase project,
        string? plcName,
        string tableName,
        string? folderPath,
        string name)
    {
        RequireName(name, "Name");

        var (plc, table) = ResolveTable(project, plcName, tableName, folderPath);
        var constant = table.UserConstants.Find(name) ??
            throw NotFound($"User constant '{name}' was not found in tag table '{tableName}'.");

        constant.Delete();
        return Result("delete_user_constant", project, plc.Name, table.Name, NormalizeFolderPath(folderPath), null, name);
    }

    /// <summary>
    /// Moves a tag to another tag table of the same PLC. Openness V21 documents no move member for a
    /// <c>PlcTag</c>, and tag names are unique per PLC across tables, so this is a guarded re-create:
    /// create the temporary tag in the target table with the source's documented attributes
    /// (<c>DataTypeName</c>, <c>LogicalAddress</c>, <c>ExternalAccessible</c>, <c>ExternalVisible</c>,
    /// <c>ExternalWritable</c>) and its multilingual <c>Comment</c> (each non-empty
    /// <c>MultilingualTextItem.Text</c>, by <c>Items.Find(Language)</c> on the new tag), delete the
    /// source, rename the temporary tag to the original name and read everything back. A failure before the source delete removes the temporary tag again; a
    /// failure after it names the temporary tag, which then holds the moved tag.
    /// </summary>
    /// <param name="inTransaction">
    /// True when the caller runs this inside an ExclusiveAccess transaction (the worker dispatch does,
    /// through <c>PlcRoundTrip.MoveTag</c>): a failure then leaves the cleanup to the rollback and the
    /// messages say so instead of naming a temporary tag to rename.
    /// </param>
    public static PlcTagMoveResultInfo MoveTag(ProjectBase project, PlcTagMoveRequestInfo move, bool inTransaction = false)
    {
        RequireName(move.Name, "Name");
        RequireName(move.TargetTableName, "TargetTableName");
        RequireName(move.TemporaryName, "TemporaryName");
        var source = TagTargetResolver.Resolve(project, move.PlcName, move.TableName, move.FolderPath, move.Name);
        var (_, targetTable) = ResolveTable(project, move.PlcName, move.TargetTableName, move.TargetFolderPath);
        var targetFolder = NormalizeFolderPath(move.TargetFolderPath);
        if (Equals(source.Table, targetTable))
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                $"move_tag: tag '{move.Name}' is already in tag table '{targetTable.Name}' ('{targetFolder}'). Nothing was changed.");

        var name = source.Tag.Name;
        var dataType = source.Tag.DataTypeName;
        var address = source.Tag.LogicalAddress ?? string.Empty;
        var notCopied = new List<string>
        {
            "Any attribute other than DataTypeName, LogicalAddress, ExternalAccessible, ExternalVisible, ExternalWritable and Comment.",
        };
        var accessible = ReadFlag(() => source.Tag.ExternalAccessible, "ExternalAccessible", notCopied);
        var visible = ReadFlag(() => source.Tag.ExternalVisible, "ExternalVisible", notCopied);
        var writable = ReadFlag(() => source.Tag.ExternalWritable, "ExternalWritable", notCopied);
        var comment = ReadComment(source.Tag, notCopied);
        // A fail-safe tag moves like any other (TIA Portal moves it too), under the safety login rule.
        if (ReadSafetyFlag(source.Tag) is true)
            Safety.PlcSafetyGate.RequireSafetyTagChangePermitted(project, move.PlcName, $"move_tag of '{name}'");
        if (targetTable.Tags.Find(move.TemporaryName) is not null)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                $"move_tag: temporary name '{move.TemporaryName}' is already used in tag table '{targetTable.Name}'. Nothing was changed; retry the call.");

        var steps = new List<string>();
        var temporary = targetTable.Tags.Create(move.TemporaryName, dataType, address);
        steps.Add($"created temporary tag '{move.TemporaryName}' ({dataType}, '{address}') in '{targetTable.Name}'");
        try
        {
            if (accessible.HasValue) temporary.ExternalAccessible = accessible.Value;
            if (visible.HasValue) temporary.ExternalVisible = visible.Value;
            if (writable.HasValue) temporary.ExternalWritable = writable.Value;
            steps.Add("copied the readable external-access flags");
            if (comment is not null)
            {
                var copied = CopyComment(temporary, comment, notCopied);
                steps.Add(copied.Count == 0
                    ? "the source comment is empty in every language; nothing to copy"
                    : $"copied the comment in {copied.Count} language(s): {string.Join(", ", copied)}");
                var unequal = CommentMismatches(temporary, comment, copied);
                if (unequal.Count > 0)
                    throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                        $"move_tag: the comment read back from the temporary tag does not match ({string.Join("; ", unequal)}). "
                        + $"The source tag '{name}' in '{source.Table.Name}' was not deleted. {(inTransaction ? RolledBack : TryDelete(temporary))}", steps);
            }
            source.Tag.Delete();
            steps.Add($"deleted source tag '{name}' from '{source.Table.Name}'");
        }
        catch (EngineeringException exception)
        {
            var cleanup = inTransaction ? RolledBack : TryDelete(temporary);
            throw new WorkerOperationException(WorkerExecution.Classify(exception, mutates: true),
                $"move_tag: {exception.Message} The source tag '{name}' in '{source.Table.Name}' was not deleted. {cleanup}",
                steps, engineeringError: WorkerExecution.Describe(exception));
        }

        try
        {
            temporary.Name = name;
        }
        catch (EngineeringException exception)
        {
            throw new WorkerOperationException(WorkerExecution.Classify(exception, mutates: true),
                $"move_tag: renaming the temporary tag failed: {exception.Message} "
                + (inTransaction ? RolledBack : $"The source tag was already deleted; the moved tag remains as '{move.TemporaryName}' in '{targetTable.Name}' ('{targetFolder}'). Rename it with update_tag."),
                steps, engineeringError: WorkerExecution.Describe(exception));
        }

        var actualName = temporary.Name;
        if (!string.Equals(actualName, name, StringComparison.Ordinal))
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"move_tag: Portal did not rename the temporary tag to '{name}'; it is still named '{actualName}' in '{targetTable.Name}' "
                + $"('{targetFolder}'). " + (inTransaction ? RolledBack : "The source tag was already deleted. Rename it with update_tag."), steps);
        steps.Add($"renamed '{move.TemporaryName}' to '{name}'");

        var moved = targetTable.Tags.Find(name);
        var mismatches = new List<string>();
        if (moved is null) mismatches.Add($"tag '{name}' is not found in '{targetTable.Name}'");
        else
        {
            if (!string.Equals(moved.DataTypeName, dataType, StringComparison.Ordinal)) mismatches.Add($"DataTypeName is '{moved.DataTypeName}', expected '{dataType}'");
            if (!string.Equals(moved.LogicalAddress ?? string.Empty, address, StringComparison.Ordinal)) mismatches.Add($"LogicalAddress is '{moved.LogicalAddress}', expected '{address}'");
            CompareFlag(() => moved.ExternalAccessible, accessible, "ExternalAccessible", mismatches);
            CompareFlag(() => moved.ExternalVisible, visible, "ExternalVisible", mismatches);
            CompareFlag(() => moved.ExternalWritable, writable, "ExternalWritable", mismatches);
            if (comment is not null)
                mismatches.AddRange(CommentMismatches(moved, comment, comment.Where(entry => entry.Copied).Select(entry => entry.Culture).ToList()));
        }

        if (source.Table.Tags.Find(name) is not null) mismatches.Add($"tag '{name}' is still present in '{source.Table.Name}'");
        if (mismatches.Count > 0)
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"move_tag: the read-back does not match: {string.Join("; ", mismatches)}." + (inTransaction ? " " + RolledBack : string.Empty), steps);
        steps.Add("read back name, data type, address, flags and comment");

        return new PlcTagMoveResultInfo
        {
            PlcName = source.PlcName,
            TagName = name,
            SourceTableName = source.Table.Name,
            SourceFolderPath = source.FolderPath,
            TargetTableName = targetTable.Name,
            TargetFolderPath = targetFolder,
            DataType = dataType,
            LogicalAddress = address,
            ExternalAccessible = accessible,
            ExternalVisible = visible,
            ExternalWritable = writable,
            TemporaryName = move.TemporaryName,
            Steps = steps,
            NotCopied = notCopied,
        };
    }

    private const string RolledBack = "The transaction is rolled back, which removes the temporary tag and restores the source tag.";

    /// <summary>One non-empty language entry of a tag comment.</summary>
    private sealed class CommentEntry
    {
        public CommentEntry(Language language, string culture, string text)
        {
            Language = language;
            Culture = culture;
            Text = text;
        }

        public Language Language { get; }
        public string Culture { get; }
        public string Text { get; }
        public bool Copied { get; set; }
    }

    /// <summary>
    /// Reads the documented multilingual <c>PlcTag.Comment</c> (<c>MultilingualText.Items</c>, each
    /// <c>MultilingualTextItem { Language, Text }</c>); null, with a notCopied entry, when unreadable.
    /// </summary>
    private static List<CommentEntry>? ReadComment(PlcTag tag, List<string> notCopied)
    {
        try
        {
            var entries = new List<CommentEntry>();
            foreach (var item in tag.Comment.Items)
            {
                var text = item.Text;
                if (string.IsNullOrEmpty(text)) continue;
                entries.Add(new CommentEntry(item.Language, CultureName(item.Language), text));
            }

            return entries;
        }
        catch (EngineeringException exception)
        {
            notCopied.Add($"Comment: unreadable on the source ({exception.Message}); the moved tag keeps an empty comment.");
            return null;
        }
    }

    /// <summary>Writes each source language text into the new tag's item of the same language; returns the cultures written.</summary>
    private static List<string> CopyComment(PlcTag target, List<CommentEntry> comment, List<string> notCopied)
    {
        var copied = new List<string>();
        foreach (var entry in comment)
        {
            var item = target.Comment.Items.Find(entry.Language);
            if (item is null)
            {
                notCopied.Add($"Comment ({entry.Culture}): the new tag has no text item for this language.");
                continue;
            }

            item.Text = entry.Text;
            entry.Copied = true;
            copied.Add(entry.Culture);
        }

        return copied;
    }

    private static List<string> CommentMismatches(PlcTag tag, List<CommentEntry> comment, List<string> cultures)
    {
        var mismatches = new List<string>();
        foreach (var entry in comment.Where(entry => cultures.Contains(entry.Culture, StringComparer.Ordinal)))
        {
            try
            {
                var actual = tag.Comment.Items.Find(entry.Language)?.Text;
                if (!string.Equals(actual, entry.Text, StringComparison.Ordinal))
                    mismatches.Add($"Comment ({entry.Culture}) is {(actual is null ? "missing" : $"'{actual}'")}, expected '{entry.Text}'");
            }
            catch (EngineeringException exception)
            {
                mismatches.Add($"Comment ({entry.Culture}) is unreadable ({exception.Message})");
            }
        }

        return mismatches;
    }

    private static string CultureName(Language language)
    {
        try
        {
            return language.Culture?.Name ?? "unknown";
        }
        catch (EngineeringException)
        {
            return "unknown";
        }
    }

    private static bool? ReadFlag(Func<bool> read, string name, List<string> notCopied)
    {
        try
        {
            return read();
        }
        catch (EngineeringException exception)
        {
            notCopied.Add($"{name}: unreadable on the source ({exception.Message}); the moved tag keeps TIA Portal's default.");
            return null;
        }
    }

    /// <summary>Documented PlcTag attribute <c>IsSafety</c>, read dynamically; null when the attribute is unreadable.</summary>
    private static bool? ReadSafetyFlag(PlcTag tag)
    {
        try
        {
            return ((IEngineeringObject)tag).GetAttribute("IsSafety") as bool?;
        }
        catch (EngineeringException)
        {
            return null;
        }
    }

    private static void CompareFlag(Func<bool> read, bool? expected, string name, List<string> mismatches)
    {
        if (expected is null) return;
        try
        {
            if (read() != expected.Value) mismatches.Add($"{name} is {(!expected.Value).ToString().ToLowerInvariant()}, expected {expected.Value.ToString().ToLowerInvariant()}");
        }
        catch (EngineeringException exception)
        {
            mismatches.Add($"{name} is unreadable ({exception.Message})");
        }
    }

    private static string TryDelete(PlcTag temporary)
    {
        try
        {
            temporary.Delete();
            return "The temporary tag was deleted again.";
        }
        catch (EngineeringException exception)
        {
            return $"Deleting the temporary tag failed as well ({exception.Message}); remove it with delete_tag.";
        }
    }

    private static (PlcSoftware Plc, PlcTagTable Table) ResolveTable(
        ProjectBase project,
        string? plcName,
        string tableName,
        string? folderPath)
    {
        RequireName(tableName, "TableName");

        var (plc, group) = ResolveGroup(project, plcName, folderPath);
        var table = group.TagTables.Find(tableName) ??
            throw NotFound($"Tag table '{tableName}' was not found in '{NormalizeFolderPath(folderPath)}'.");
        return (plc, table);
    }

    private static (PlcSoftware Plc, PlcTagTableGroup Group) ResolveGroup(
        ProjectBase project,
        string? plcName,
        string? folderPath)
    {
        var plcSoftware = PlcSoftwareLocator.FindUnique(project, plcName).Software;
        PlcTagTableGroup group = plcSoftware.TagTableGroup;

        foreach (var segment in SplitFolderPath(folderPath))
        {
            group = group.Groups.Find(segment) ??
                throw NotFound($"Tag table folder '{NormalizeFolderPath(folderPath)}' was not found.");
        }

        return (plcSoftware, group);
    }

    private static WorkerOperationException NotFound(string message)
        => new(WorkerFailureCategories.TargetNotFound, message);

    private static string[] SplitFolderPath(string? folderPath)
    {
        var trimmed = folderPath?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed == "/")
        {
            return Array.Empty<string>();
        }

        return trimmed!
            .Trim('/')
            .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static string NormalizeFolderPath(string? folderPath)
    {
        var segments = SplitFolderPath(folderPath);
        return segments.Length == 0
            ? "/"
            : "/" + string.Join("/", segments);
    }

    private static void RequireName(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{fieldName} is required.");
        }
    }

    private static TagMutationResultInfo Result(
        string operation,
        ProjectBase project,
        string plcName,
        string tableName,
        string folderPath,
        string? tagName,
        string? userConstantName)
    {
        return new TagMutationResultInfo
        {
            Operation = operation,
            ProjectPath = project.Path?.FullName,
            PlcName = plcName,
            TableName = tableName,
            FolderPath = folderPath,
            TagName = tagName,
            UserConstantName = userConstantName
        };
    }
}
