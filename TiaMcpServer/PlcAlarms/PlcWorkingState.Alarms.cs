using TiaMcpServer.Contracts.PlcAlarms;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Safety.Pipeline;
using static TiaMcpServer.Plc.PlcGuardDefinitions;

namespace TiaMcpServer.Plc;

/// <summary>
/// The text list, alarm text and supervision part of the planner's working state. Text lists come from one
/// <c>list_text_lists</c> read of the PLC, only when an item needs it (<see cref="NeedsTextLists"/>). Update and
/// delete targets are resolved (missing or ambiguous is an error, a system list is refused); an earlier import or
/// cell update of the same call may create, rename or remove lists, so a later list item that misses its target
/// then depends on it and is re-planned against the real state before its mutation. Workbook cell updates and file
/// operations get their evidence from the planner (the workbook probe and the file system).
/// </summary>
internal sealed partial class PlcWorkingState
{
    public const string TextListKind = "TextList";
    public const string TextListWorkbookKind = "TextListWorkbook";
    public const string AlarmTextsKind = "AlarmTexts";
    public const string SupervisionsKind = "Supervisions";
    public const string AlarmClassesKind = "AlarmClasses";
    public const string SystemDiagnosticsSettingsKind = "SystemDiagnosticsSettings";
    public const string ProDiagAssignmentKind = "ProDiagAssignment";
    public const string ProDiagInfoKind = "ProDiagInfo";

    private sealed record TextList(string Name, string? ListRange, IReadOnlyList<PlcAlarmTextItemInfo>? Comment);

    private List<TextList>? _textLists;
    private List<string>? _systemTextLists;
    private bool _textListsComplete = true;
    private string? _textListsReplacedBy;

    /// <summary>Text list updates and deletes, and a cell update that names text lists (its names are checked against the inventory).</summary>
    public static bool NeedsTextLists(PlcOperationRequest item)
        => item.Operation is PlcAlarmMethods.UpdateTextList or PlcAlarmMethods.DeleteTextList
            || item.Operation == PlcAlarmMethods.UpdateTextListCells && item.TextListNames is not null;

    /// <summary>True when an earlier item of the call writes to this PLC (its effects are in this state, not in the project yet).</summary>
    public bool HasEarlierWrites => _touches.Count > 0;

    public static bool IsAlarmOperation(string operation) => PlcAlarmMethods.IsWriteMethod(operation);

    /// <summary>Attaches the PLC's text list inventory, or null when no item of the call needs it.</summary>
    public PlcWorkingState WithTextLists(PlcTextListPlcInfo? inventory)
    {
        if (inventory is null) return this;
        _textLists = inventory.UserTextLists.Select(list => new TextList(list.Name, list.ListRange, list.Comment)).ToList();
        _systemTextLists = inventory.SystemTextLists.Select(list => list.Name).ToList();
        _textListsComplete = inventory.Complete;
        return this;
    }

    /// <summary>The kind of the target an alarm operation addresses (the effect's <c>target.kind</c>).</summary>
    public static string AlarmTargetKind(string operation) => operation switch
    {
        PlcAlarmMethods.UpdateTextList or PlcAlarmMethods.DeleteTextList => TextListKind,
        PlcAlarmMethods.SetProDiagAssignment => ProDiagAssignmentKind,
        PlcAlarmMethods.ExportProDiagInfo => ProDiagInfoKind,
        _ => PlcAlarmMethods.WorkbookKind(operation) switch
        {
            PlcAlarmPolicy.WorkbookKinds.TextLists => TextListWorkbookKind,
            PlcAlarmPolicy.WorkbookKinds.AlarmTexts => AlarmTextsKind,
            PlcAlarmPolicy.WorkbookKinds.AlarmClasses => AlarmClassesKind,
            PlcAlarmPolicy.WorkbookKinds.SystemDiagnosticsSettings => SystemDiagnosticsSettingsKind,
            _ => SupervisionsKind,
        },
    };

    private PlcWriteEffect ResolveAlarm(PlcOperationRequest item, List<FiredGuard> guards)
    {
        switch (item.Operation)
        {
            case PlcAlarmMethods.UpdateTextList:
            case PlcAlarmMethods.DeleteTextList:
            {
                if (_textLists is null)
                    throw new InvalidOperationException($"Operation '{item.OperationId}' ({item.Operation}) needs the text lists of PLC '{_plc}', which were not read.");
                if (!_textListsComplete)
                    guards.Add(Fire(StateUnverifiable, item, $"The text list inventory of PLC '{_plc}' is incomplete, so the target cannot be verified."));
                if (_systemTextLists!.Any(name => Names.Equals(name, item.TextListName)) && !_textLists.Any(list => Names.Equals(list.Name, item.TextListName)))
                    throw new ResolutionException(WorkerFailureCategories.ValidationError,
                        $"Operation '{item.OperationId}': '{item.TextListName}' is a system text list of PLC '{_plc}'; system text lists are not changed through this server.");
                var matches = _textLists.Where(list => Names.Equals(list.Name, item.TextListName)).Take(2).ToList();
                if (matches.Count > 1)
                    throw new ResolutionException(WorkerFailureCategories.TargetAmbiguous, $"Operation '{item.OperationId}': user text list '{item.TextListName}' is ambiguous in PLC '{_plc}'.");
                if (matches.Count == 0)
                {
                    // An earlier import or cell update of this call may have created it: re-planned before the mutation.
                    if (_textListsReplacedBy is null)
                        throw NotFound(item, $"user text list '{item.TextListName}' was not found in PLC '{_plc}'.", !_textListsComplete);
                    return Effect(item, AlarmTarget(item, item.TextListName), RequestedTextListChanges(item, null), null);
                }

                var list = matches[0];
                // TIA Portal compares text list names case-insensitively and may ignore a colliding rename silently (like a tag rename).
                if (item.NewName is { } newName && !Names.Equals(newName, list.Name)
                    && (_textLists.FirstOrDefault(other => !ReferenceEquals(other, list) && Names.Equals(other.Name, newName)) is { } taken
                        ? $"user text list '{taken.Name}'" : _systemTextLists!.FirstOrDefault(system => Names.Equals(system, newName)) is { } system ? $"system text list '{system}'" : null)
                        is { } collision)
                    throw new ResolutionException(WorkerFailureCategories.ValidationError,
                        $"Operation '{item.OperationId}': user text list '{list.Name}' cannot be renamed to '{newName}': PLC '{_plc}' already has {collision} "
                        + "(TIA Portal compares names case-insensitively).");
                if (item.Operation == PlcAlarmMethods.DeleteTextList)
                {
                    guards.Add(Fire(DeletesTextList, item, $"Deleting user text list '{list.Name}' of PLC '{_plc}' removes it and its entries."));
                    return Effect(item, AlarmTarget(item, list.Name), Array.Empty<PlcFieldChange>(), null);
                }

                return Effect(item, AlarmTarget(item, list.Name), RequestedTextListChanges(item, list), null);
            }
            case PlcAlarmMethods.SetProDiagAssignment:
                return Effect(item, AlarmTarget(item, AssignmentDisplay(item)), Array.Empty<PlcFieldChange>(), null);
            case PlcAlarmMethods.ExportProDiagInfo:
                // A new directory outside the project; the planner checks it does not exist yet.
                return Effect(item, AlarmTarget(item, null), Array.Empty<PlcFieldChange>(), item.DirectoryPath);
            default:
            {
                var target = AlarmTarget(item, null);
                if (item.Operation == PlcAlarmMethods.UpdateTextListCells && item.TextListNames is { } names && _textLists is not null)
                {
                    // F1: the cell update imports the workbook again; a system text list is never written through this server.
                    foreach (var name in names)
                    {
                        if (_textLists.Any(list => Names.Equals(list.Name, name))) continue;
                        if (_systemTextLists!.FirstOrDefault(system => Names.Equals(system, name)) is { } system)
                            throw new ResolutionException(WorkerFailureCategories.ValidationError,
                                $"Operation '{item.OperationId}': '{system}' is a system text list of PLC '{_plc}'; update_text_list_cells imports the workbook again and "
                                + "system text lists are not changed through this server. Leave it out of textListNames (read_text_list_workbook reads it).");
                        if (_textListsComplete)
                            throw NotFound(item, $"text list '{name}' was not found in PLC '{_plc}'.", false);
                    }
                }

                if (PlcAlarmMethods.CellUpdates.Contains(item.Operation))
                {
                    guards.Add(Fire(WorkbookRoundTrip, item, $"{item.Operation} exports the {Describe(target)} again, refuses a changed workbookHash, patches "
                        + $"{item.CellEdits?.Count ?? 0} cell(s), imports the exported workbook with them (every row it holds) through TIA Portal's XLSX import and "
                        + "re-exports it, all inside ExclusiveAccess; the item fails when the re-export misses an edit or shows any other changed cell, and the project is then changed."));
                    return Effect(item, target, Array.Empty<PlcFieldChange>(), null);
                }

                if (PlcAlarmMethods.FileImports.Contains(item.Operation))
                {
                    var readBack = item.Operation == PlcAlarmMethods.ImportSupervisionSettings || item.Operation == PlcAlarmMethods.ImportAlarmTexts && item.ExportOption is null
                        ? "it is not read back: no documented export returns this data, so only TIA Portal's reported state is checked."
                        : item.Operation is PlcAlarmMethods.ImportAlarmClasses or PlcAlarmMethods.ImportSystemDiagnosticsSettings
                            ? "a re-export right after it is compared with the file byte for byte."
                            : "a re-export of the same kind right after it shows whether every imported row came back (columns matched by header).";
                    guards.Add(Fire(ImportsAlarmData, item, $"{item.Operation} imports a private copy of '{item.FilePath}' into the {Describe(target)}; what TIA Portal's import "
                        + $"replaces, adds or keeps is decided by TIA Portal; {readBack}"));
                    return Effect(item, target, Change("filePath", null, item.FilePath).Concat(Change("importOption", null, ImportOptionOf(item))).ToArray(), null);
                }

                // An export writes a file outside the project; the planner adds the overwrite guard.
                return Effect(item, target, Array.Empty<PlcFieldChange>(), item.FilePath);
            }
        }
    }

    private static string? ImportOptionOf(PlcOperationRequest item)
        => item.Operation is PlcAlarmMethods.ImportTextLists or PlcAlarmMethods.ImportSupervisions or PlcAlarmMethods.ImportSupervisionSettings
            ? item.ImportOption ?? PlcAlarmPolicy.DefaultImportOption : null;

    private static PlcFieldChange[] RequestedTextListChanges(PlcOperationRequest item, TextList? list)
    {
        var comment = item.CommentCulture is null ? null
            : list?.Comment?.FirstOrDefault(text => string.Equals(text.Culture, item.CommentCulture, StringComparison.OrdinalIgnoreCase))?.Text;
        return Change("name", list?.Name, item.NewName)
            .Concat(Change("listRange", list?.ListRange, item.ListRange))
            .Concat(item.CommentCulture is null ? Array.Empty<PlcFieldChange>() : Change($"comment[{item.CommentCulture}]", comment, item.Comment ?? string.Empty))
            .ToArray();
    }

    private void ApplyAlarm(PlcOperationRequest item)
    {
        switch (item.Operation)
        {
            case PlcAlarmMethods.UpdateTextList:
            {
                Touch(TextListKey(item.TextListName!), item);
                var list = _textLists?.FirstOrDefault(candidate => Names.Equals(candidate.Name, item.TextListName));
                if (item.NewName is { } newName)
                {
                    Touch(TextListKey(newName), item);
                    if (list is not null) _textLists![_textLists.IndexOf(list)] = list with { Name = newName };
                }

                break;
            }
            case PlcAlarmMethods.DeleteTextList:
            {
                Touch(TextListKey(item.TextListName!), item);
                var list = _textLists?.FirstOrDefault(candidate => Names.Equals(candidate.Name, item.TextListName));
                if (list is not null) _textLists!.Remove(list);
                break;
            }
            case PlcAlarmMethods.ImportTextLists:
            case PlcAlarmMethods.UpdateTextListCells:
                _textListsReplacedBy = item.OperationId;
                Touch(TextListsKey, item);
                break;
            case PlcAlarmMethods.ImportAlarmTexts:
            case PlcAlarmMethods.UpdateAlarmTextCells:
                Touch(AlarmTextsKey, item);
                break;
            case PlcAlarmMethods.ImportSupervisions:
            case PlcAlarmMethods.ImportSupervisionSettings:
            case PlcAlarmMethods.UpdateSupervisionCells:
                Touch(SupervisionKey(item.BlockPath), item);
                break;
            case PlcAlarmMethods.SetProDiagAssignment:
                Touch(AssignmentKey(AssignmentDisplay(item)), item);
                break;
        }
    }

    /// <summary>The AssignedProDiagFB target as the request names it: the tag (folder/table/name) or the block path (and member).</summary>
    public static string AssignmentDisplay(PlcOperationRequest item)
        => item.TableName is not null ? $"{(item.FolderPath ?? "/").TrimEnd('/')}/{item.TableName}/{item.Name}"
            : item.MemberPath is null ? item.BlockPath ?? string.Empty : $"{item.BlockPath}.{item.MemberPath}";

    /// <summary>The keys an alarm target depends on: a text list on its own name and on every earlier whole-workbook change.</summary>
    private static IEnumerable<string> AlarmKeys(PlcTargetIdentity target) => target.Kind switch
    {
        TextListKind when target.Name is not null => new[] { TextListKey(target.Name), TextListsKey },
        TextListWorkbookKind => new[] { TextListsKey },
        AlarmTextsKind => new[] { AlarmTextsKey },
        SupervisionsKind => new[] { SupervisionKey(target.BlockPath) },
        ProDiagAssignmentKind when target.Name is not null => new[] { AssignmentKey(target.Name) },
        _ => Array.Empty<string>(),
    };

    private PlcTargetIdentity AlarmTarget(PlcOperationRequest item, string? name)
        => new(AlarmTargetKind(item.Operation), _plc, _device, null, null, name, UsesBlock(item) ? item.BlockPath : null, null);

    private static bool UsesBlock(PlcOperationRequest item)
        => PlcAlarmMethods.UsesBlockPath(item.Operation) || item.Operation == PlcAlarmMethods.SetProDiagAssignment && item.BlockPath is not null;

    /// <summary>The requested identity of an alarm item whose PLC could not be verified.</summary>
    private static PlcTargetIdentity? UnresolvedAlarmTarget(PlcOperationRequest item, string plcName, string? deviceName)
        => IsAlarmOperation(item.Operation)
            ? new(AlarmTargetKind(item.Operation), plcName, deviceName, null, null,
                item.Operation == PlcAlarmMethods.SetProDiagAssignment ? AssignmentDisplay(item) : NeedsTextLists(item) ? item.TextListName : null,
                UsesBlock(item) ? item.BlockPath : null, null)
            : null;

    private static string Describe(PlcTargetIdentity target) => target.Kind switch
    {
        TextListWorkbookKind => $"text list workbook of PLC '{target.PlcName}'",
        AlarmTextsKind => $"alarm text workbook of PLC '{target.PlcName}'",
        SupervisionsKind => $"supervisions of FB '{target.BlockPath}'",
        ProDiagInfoKind => $"ProDiag alarm information of FB '{target.BlockPath}'",
        _ => target.Kind,
    };

    private const string TextListsKey = "alarm:textlists";
    private const string AlarmTextsKey = "alarm:texts";

    private static string TextListKey(string name) => "alarm:textlist:" + name.ToUpperInvariant();

    private static string SupervisionKey(string? blockPath) => "alarm:supervision:" + (blockPath ?? string.Empty).ToUpperInvariant();

    private static string AssignmentKey(string target) => "alarm:prodiag:" + target.ToUpperInvariant();
}
