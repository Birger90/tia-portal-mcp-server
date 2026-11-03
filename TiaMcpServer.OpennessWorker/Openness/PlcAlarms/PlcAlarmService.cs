using Siemens.Engineering;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcAlarms;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.Libraries;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.PlcAlarms;

/// <summary>
/// PLC alarm texts, alarm classes, text lists, ProDiag supervisions and the ProDiag members of blocks.md. Every Siemens member
/// comes from the V21 references: <c>PlcSoftware.PlcAlarmTextlistGroup</c>, <c>PlcAlarmUserTextlist.Delete()</c>, the XLSX
/// providers (bound before any call by <see cref="PlcAlarmTransfers"/>), <c>AlarmClassDataProvider.Export</c>/<c>Import</c>
/// and <see cref="PlcProDiagService"/>. The orchestration (temporary files, states, hashes, cell patch, verification
/// re-exports, caller files) is the Siemens-free <see cref="PlcAlarmWorkbookRun"/>; a cell update or import runs inside
/// <c>TiaPortal.ExclusiveAccess</c> (no transaction: whether the XLSX imports may run in one is not documented). Every write
/// records the TIA Portal confirmation dialogs its <c>portalConfirmations</c> policy answered; a failure carries the item's
/// result as evidence (<see cref="WorkerOperationException.PlcDownloadOutcome"/>).
/// </summary>
internal static class PlcAlarmService
{
    // ================================================================ reads

    public static PlcTextListInventoryInfo ListTextLists(ProjectBase project, PlcAlarmRequestInfo request)
    {
        var failures = new List<string>();
        var plcs = PlcSoftwareLocator.FindEveryPlc(project, request.PlcName, failures).ToList();
        if (failures.Count > 0)
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, failures[0] + " No text lists were listed.");
        if (request.PlcName is not null && plcs.Count == 0)
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No PLC software named '{request.PlcName}' was found in the project.");

        var inventory = new PlcTextListInventoryInfo { ProjectLanguages = Languages(project) };
        foreach (var plc in plcs)
        {
            var info = new PlcTextListPlcInfo { PlcName = plc.Software.Name ?? string.Empty, DeviceName = plc.DeviceName ?? string.Empty };
            var group = plc.Software.PlcAlarmTextlistGroup;
            if (group is null)
            {
                info.Complete = false;
                info.Messages.Add("The PLC offers no PlcAlarmTextlistGroup.");
            }
            else
            {
                foreach (var list in group.PlcAlarmUserTextlists)
                {
                    if (!Room(info)) break;
                    info.UserTextLists.Add(PlcTextListResolver.Describe(list, isSystem: false, list.Name));
                }

                foreach (var list in group.PlcAlarmSystemTextlists)
                {
                    if (!Room(info)) break;
                    info.SystemTextLists.Add(PlcTextListResolver.Describe(list, isSystem: true, PlcTextListResolver.SystemName(list)));
                }
            }

            inventory.Plcs.Add(info);
        }

        return inventory;
    }

    /// <summary>
    /// read_text_list_workbook, read_alarm_text_workbook, read_supervision_workbook: export to a temporary file, read the
    /// cells within the budget, delete it. Every TIA Portal dialog during the export is answered No (a read never accepts)
    /// and fails the read, because the export may then be incomplete.
    /// </summary>
    public static PlcAlarmWorkbookInfo ReadWorkbook(ProjectBase project, string method, PlcAlarmRequestInfo request, PlcDownloadPortalConfirmationLog confirmations)
    {
        var transfers = PlcAlarmTransfers.For(project, method, request);
        var info = new PlcAlarmWorkbookInfo { Kind = transfers.Kind };
        Describe(info, transfers);
        var run = new PlcAlarmWorkbookRun();
        try
        {
            run.Read(transfers.Export!, info, new XlsxProjection
            {
                Sheet = request.Sheet,
                StartRow = request.StartRow,
                StartColumn = request.StartColumn is null ? 1 : PlcAlarmPolicy.ParseColumn(request.StartColumn) ?? 0,
                MaxRows = request.MaxRows,
                MaxChars = request.MaxResultChars,
            }, request.Probes);
        }
        catch (PlcAlarmException exception)
        {
            throw new WorkerOperationException(exception.Category, exception.Message);
        }
        finally
        {
            Emit(transfers.Warnings.Concat(run.Warnings));
        }

        var (observed, types) = (0, new List<string>());
        confirmations.CloseInto((entries, count, _) => (observed, types) = (count, entries.Select(e => e.Type).Distinct().Take(5).ToList()));
        if (observed > 0)
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                $"TIA Portal raised {observed} confirmation dialog(s) during the export of {method} ({string.Join(", ", types)}); "
                + "a read answers every dialog No, so the exported workbook may be incomplete and is not returned. Nothing was changed.");
        return info;
    }

    // ================================================================ writes

    /// <param name="confirmations">
    /// The TIA Portal confirmations answered while this item runs (the dispatch opened the scope with the item's
    /// <c>portalConfirmations</c> policy); every exit closes the scope first and then copies the record into the result.
    /// </param>
    /// <param name="portalDisposalRevision">
    /// The session's Portal disposal revision: a Siemens failure of the verification export after an import is tolerated
    /// (inconclusive verification) only while it is unchanged, so a lost Portal still fails as portal_lost.
    /// </param>
    public static PlcAlarmWriteResultInfo Write(TiaPortal portal, ProjectBase project, string method, PlcAlarmRequestInfo request,
        PlcDownloadPortalConfirmationLog? confirmations = null, Func<long>? portalDisposalRevision = null)
    {
        var policy = request.PortalConfirmations ?? PlcDownloadPolicy.PortalConfirmationPolicies.Decline;
        if (!PlcDownloadPolicy.PortalConfirmationPolicies.IsKnown(policy))
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                $"portalConfirmations must be one of: {string.Join(", ", PlcDownloadPolicy.PortalConfirmationPolicies.All)}.");
        var result = new PlcAlarmWriteResultInfo { Operation = method, PortalConfirmationPolicy = policy };
        var run = new PlcAlarmWorkbookRun(exclusive: () => Exclusive(portal, method));
        var warnings = new List<string>();
        try
        {
            Mutate(project, method, request, result, run, warnings, portalDisposalRevision);
        }
        catch (NonRecoverableException exception)
        {
            throw Failure(WorkerFailureCategories.PortalLost,
                $"The TIA Portal connection was lost during {method}: {exception.Message}. Restart TIA Portal and explicitly bind_project again. "
                + "An attempted change may have completed; inspect it before retrying.", null, null, result, confirmations);
        }
        catch (EngineeringException exception)
        {
            var category = WorkerExecution.Classify(exception, mutates: true);
            throw Failure(category, WorkerExecution.FailureMessage(exception, category) + Applied(result), null, WorkerExecution.Describe(exception), result, confirmations);
        }
        catch (PlcAlarmException exception)
        {
            throw Failure(exception.Category, exception.Message, null, null, result, confirmations);
        }
        catch (WorkerOperationException exception) when (exception.PlcDownloadOutcome is null)
        {
            throw Failure(exception.FailureCategory, exception.Message, exception.Warnings, exception.EngineeringError, result, confirmations);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw Failure(WorkerFailureCategories.WorkerOperationFailed, exception.Message, null, null, result, confirmations);
        }
        finally
        {
            Emit(warnings.Concat(run.Warnings));
        }

        Closed(result, confirmations);
        return result;
    }

    private static void Mutate(ProjectBase project, string method, PlcAlarmRequestInfo request, PlcAlarmWriteResultInfo result, PlcAlarmWorkbookRun run,
        List<string> warnings, Func<long>? portalDisposalRevision)
    {
        switch (method)
        {
            case PlcAlarmMethods.UpdateTextList:
                UpdateTextList(project, request, result);
                return;
            case PlcAlarmMethods.DeleteTextList:
                DeleteTextList(project, request, result);
                return;
            case PlcAlarmMethods.SetProDiagAssignment:
                PlcProDiagService.SetAssignment(project, request, result);
                return;
            case PlcAlarmMethods.ExportProDiagInfo:
            {
                var (_, export) = PlcProDiagService.PrepareExport(project, request, result, warnings);
                run.ExportDirectory(export, request.DirectoryPath, result);
                return;
            }
        }

        var transfers = PlcAlarmTransfers.For(project, method, request);
        Describe(result, transfers);
        warnings.AddRange(transfers.Warnings);
        if (PlcAlarmMethods.CellUpdates.Contains(method))
        {
            run.UpdateCells(transfers.Export!, transfers.Import!, request.CellEdits, request.ExpectedWorkbookHash, PlcAlarmMethods.WorkbookReadFor(method)!, result);
            return;
        }

        // The alarm classes and the system diagnostics settings: undocumented file formats, byte compare, Warning succeeds.
        var classes = PlcAlarmPolicy.WorkbookKinds.IsOpaqueFile(transfers.Kind);
        var extension = PlcAlarmPolicy.RequiredExtension(method);
        if (PlcAlarmMethods.FileExports.Contains(method))
        {
            run.ExportFile(transfers.Export!, request.FilePath, request.Overwrite, extension, warningIsSuccess: classes, result);
            return;
        }

        var reason = method switch
        {
            PlcAlarmMethods.ImportSupervisionSettings => "no documented export reads supervision settings back (only ExportSupervisionsToXlsx exists).",
            PlcAlarmMethods.ImportAlarmTexts when transfers.Export is null => "the request names no exportOption to re-export the alarm texts with.",
            _ => null,
        };
        var verificationExport = transfers.Export;
        PlcAlarmWorkbookRun.Transfer? reexport = verificationExport is null ? null : path => VerificationExport(verificationExport, path, portalDisposalRevision);
        run.ImportFile(transfers.Import!, reexport, request.FilePath, extension, warningIsSuccess: classes, compareBytes: classes, reason, result);
    }

    /// <summary>
    /// The verification export after an import, which has already changed the project: a recoverable Siemens failure becomes
    /// an unreadable outcome (the verification is inconclusive, the import is not failed); a lost Portal (a
    /// <see cref="NonRecoverableException"/>, or a Portal disposal observed during the export) still escapes.
    /// </summary>
    private static PlcAlarmResultReader.Outcome VerificationExport(PlcAlarmWorkbookRun.Transfer export, string path, Func<long>? portalDisposalRevision)
    {
        var before = portalDisposalRevision?.Invoke();
        try
        {
            return export(path);
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception) && portalDisposalRevision?.Invoke() == before)
        {
            var outcome = new PlcAlarmResultReader.Outcome();
            outcome.Unreadable.Add($"the verification export threw {exception.GetType().Name}: {PlcAlarmPolicy.Bound(exception.Message, 500)}");
            return outcome;
        }
    }

    /// <summary>
    /// update_text_list: every requested member is checked before the first change (writable Name/ListRange, a free new name,
    /// the enum member, the comment item), then the rename runs first and is read back (TIA Portal may ignore a colliding
    /// rename silently, like a tag rename), then ListRange, then the comment. A failure names the members already applied.
    /// </summary>
    public static PlcAlarmWriteResultInfo UpdateTextList(ProjectBase project, PlcAlarmRequestInfo request, PlcAlarmWriteResultInfo result)
    {
        var plan = PrepareTextListUpdate(project, request);
        result.PlcName = plan.Plc.Software.Name;
        result.DeviceName = plan.Plc.DeviceName;
        result.TextListName = plan.Name;
        result.AppliedMembers = new List<string>();
        if (request.NewName is not null)
        {
            LibraryReflection.Rename(plan.List, request.NewName, plan.Description);
            result.AppliedMembers.Add("Name");
        }

        if (plan.Range is not null)
        {
            plan.Engineering.SetAttribute("ListRange", plan.Range);
            var after = PlcTextListResolver.Text(PlcTextListResolver.Member(plan.List, "ListRange", null));
            if (!string.Equals(after, request.ListRange, StringComparison.Ordinal))
                throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                    $"{plan.Description} reads back ListRange '{after}' after SetAttribute(\"ListRange\", {request.ListRange}); TIA Portal did not apply it." + Applied(result));
            result.AppliedMembers.Add("ListRange");
        }

        if (plan.Item is not null)
        {
            plan.Item.Text = request.Comment ?? string.Empty;
            if (!string.Equals(plan.Item.Text ?? string.Empty, request.Comment ?? string.Empty, StringComparison.Ordinal))
                throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                    $"{plan.Description}: the {request.CommentCulture} comment does not read back the written text." + Applied(result));
            result.AppliedMembers.Add($"Comment[{plan.Item.Language.Culture.Name}]");
        }

        result.TextListAfter = PlcTextListResolver.Describe(plan.List, isSystem: false, plan.List.Name);
        return result;
    }

    internal sealed record TextListUpdate(PlcSoftwareLocator.DiscoveredPlcSoftware Plc, Siemens.Engineering.SW.Alarm.TextLists.PlcAlarmUserTextlist List,
        string Name, string Description, IEngineeringObject Engineering, object? Range, MultilingualTextItem? Item, List<string> Bound);

    /// <summary>The checks of update_text_list, without any change (also the planning evidence of plc_alarm_plan).</summary>
    public static TextListUpdate PrepareTextListUpdate(ProjectBase project, PlcAlarmRequestInfo request)
    {
        var plc = PlcSoftwareLocator.FindUnique(project, request.PlcName);
        var list = PlcTextListResolver.User(plc, request.TextListName);
        var name = list.Name;
        var description = $"User text list '{name}' of PLC '{plc.Software.Name}'";
        var engineering = (IEngineeringObject)list;
        var bound = new List<string>();
        if (request.NewName is null && request.ListRange is null && request.CommentCulture is null)
            throw Invalid("update_text_list needs newName, listRange or comment with commentCulture.");
        if (request.NewName is not null)
        {
            if (PlcAlarmPolicy.CheckName(request.NewName, "newName") is { } invalid) throw Invalid(invalid);
            if (PlcTextListResolver.Taken(plc, request.NewName, list) is { } taken)
                throw Invalid($"{description} cannot be renamed to '{request.NewName}': PLC '{plc.Software.Name}' already has {taken} (TIA Portal compares names case-insensitively). Nothing was changed.");
            PlcTextListResolver.RequireWritable(engineering, "Name", description);
            bound.Add("IEngineeringObject.SetAttribute(\"Name\") (writable per GetAttributeInfos)");
        }

        object? range = null;
        if (request.ListRange is not null)
        {
            PlcTextListResolver.RequireWritable(engineering, "ListRange", description);
            var current = PlcTextListResolver.Member(list, "ListRange", null) ?? throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
                $"{description}: ListRange cannot be read, so its enum type is unknown. Nothing was changed.");
            if (!current.GetType().IsEnum)
                throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
                    $"{description}: ListRange is a {current.GetType().FullName}, not an enum. Nothing was changed.");
            range = LibraryRuntimeBinding.EnumValue(current.GetType(), request.ListRange) ?? throw Invalid(
                $"'{request.ListRange}' is not a {current.GetType().Name} member on this TIA Portal installation (members: {LibraryRuntimeBinding.MemberList(current.GetType())}). Nothing was changed.");
            bound.Add($"IEngineeringObject.SetAttribute(\"ListRange\", {current.GetType().Name}.{request.ListRange})");
        }

        MultilingualTextItem? item = null;
        if (request.CommentCulture is not null)
        {
            if (PlcAlarmPolicy.CheckText(request.Comment ?? string.Empty, "comment") is { } invalidText) throw Invalid(invalidText);
            if (PlcTextListResolver.Member(list, "Comment", null) is not MultilingualText comment)
                throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
                    $"{description}: Comment is not readable as a MultilingualText. Nothing was changed.");
            var items = comment.Items.ToList();
            item = items.FirstOrDefault(candidate => string.Equals(candidate.Language.Culture.Name, request.CommentCulture, StringComparison.OrdinalIgnoreCase))
                ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                    $"{description}: the comment has no text item for culture '{request.CommentCulture}' (items: {string.Join(", ", items.Select(i => i.Language.Culture.Name))}). "
                    + "Activate the language in the project first. Nothing was changed.");
            bound.Add($"MultilingualTextItem.Text ({item.Language.Culture.Name})");
        }

        return new TextListUpdate(plc, list, name, description, engineering, range, item, bound);
    }

    public static PlcAlarmWriteResultInfo DeleteTextList(ProjectBase project, PlcAlarmRequestInfo request, PlcAlarmWriteResultInfo result)
    {
        var plc = PlcSoftwareLocator.FindUnique(project, request.PlcName);
        var list = PlcTextListResolver.User(plc, request.TextListName);
        var name = list.Name;
        result.PlcName = plc.Software.Name;
        result.DeviceName = plc.DeviceName;
        result.TextListName = name;
        list.Delete();
        if (PlcTextListResolver.Group(plc).PlcAlarmUserTextlists.Any(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"Delete() returned, but user text list '{name}' is still listed in PLC '{plc.Software.Name}'.");
        return result;
    }

    // ================================================================ planning

    /// <summary>
    /// plc_alarm_plan: resolves the write item and binds every Siemens member it will call, without calling anything;
    /// reports the caller path as the file system sees it (network drive, links) and the resolved names.
    /// </summary>
    public static PlcAlarmPlanInfo Plan(ProjectBase project, PlcAlarmRequestInfo request)
    {
        var method = request.PlannedOperation;
        if (!PlcAlarmMethods.IsWriteMethod(method)) throw Invalid($"plc_alarm_plan plans a plc_write alarm operation, not '{method}'.");
        var plan = new PlcAlarmPlanInfo { Operation = method! };
        var warnings = new List<string>();
        switch (method)
        {
            case PlcAlarmMethods.UpdateTextList:
            {
                var update = PrepareTextListUpdate(project, request);
                (plan.PlcName, plan.DeviceName) = (update.Plc.Software.Name, update.Plc.DeviceName);
                plan.Bound.AddRange(update.Bound);
                break;
            }
            case PlcAlarmMethods.DeleteTextList:
            {
                var plc = PlcSoftwareLocator.FindUnique(project, request.PlcName);
                _ = PlcTextListResolver.User(plc, request.TextListName);
                (plan.PlcName, plan.DeviceName) = (plc.Software.Name, plc.DeviceName);
                plan.Bound.Add("PlcAlarmUserTextlist.Delete()");
                break;
            }
            case PlcAlarmMethods.SetProDiagAssignment:
            {
                var target = PlcProDiagService.ResolveAssignmentTarget(project, request);
                plan.PlcName = target.PlcName;
                var current = PlcProDiagService.ReadAssignment(target);
                plan.AssignedProDiagFb = current.Name;
                if (!current.Readable) warnings.Add($"{target.Display}: the current assignment is treated as unset; {current.Message}");
                plan.Bound.Add($"IEngineeringObject.SetAttribute(\"{PlcAlarmPolicy.AssignedProDiagFbAttribute}\") on {target.Display}");
                break;
            }
            case PlcAlarmMethods.ExportProDiagInfo:
            {
                var (fb, _) = PlcProDiagService.PrepareExport(project, request, null, warnings);
                plan.BlockPath = request.BlockPath;
                (plan.ProgrammingLanguage, plan.SupervisionCount) = PlcProDiagService.Evidence(fb);
                plan.Bound.Add(PlcProDiagService.Bound);
                if (PlcAlarmPolicy.CheckDirectoryPath(request.DirectoryPath) is { } invalid) throw Invalid(invalid);
                plan.Path = PlcAlarmFiles.Inspect(request.DirectoryPath!);
                break;
            }
            default:
            {
                var transfers = PlcAlarmTransfers.For(project, method!, request);
                (plan.PlcName, plan.DeviceName, plan.BlockPath) = (transfers.Plc?.Software.Name, transfers.Plc?.DeviceName, transfers.BlockPath);
                (plan.ProgrammingLanguage, plan.SupervisionCount) = (transfers.ProgrammingLanguage, transfers.SupervisionCount);
                plan.TextListNames = transfers.TextListNames;
                plan.Bound.AddRange(transfers.Bound);
                warnings.AddRange(transfers.Warnings);
                if (request.FilePath is not null)
                {
                    if (PlcAlarmPolicy.CheckFilePath(request.FilePath, PlcAlarmPolicy.RequiredExtension(method)) is { } invalid) throw Invalid(invalid);
                    plan.Path = PlcAlarmFiles.Inspect(request.FilePath);
                }

                break;
            }
        }

        plan.Messages.AddRange(warnings);
        return plan;
    }

    // ================================================================ helpers

    /// <summary>Exclusive access for one round trip; a refusal (another exclusive operation runs) changes nothing.</summary>
    private static IDisposable Exclusive(TiaPortal portal, string method)
    {
        try
        {
            return portal.ExclusiveAccess($"TIA MCP Server: {method}");
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
        {
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                $"TIA Portal refused exclusive access for {method} ({exception.Message}); another exclusive operation may be running. Nothing was changed.");
        }
    }

    private static void Describe(PlcAlarmWorkbookInfo info, PlcAlarmTransfers transfers)
    {
        info.PlcName = transfers.Plc?.Software.Name;
        info.DeviceName = transfers.Plc?.DeviceName;
        info.BlockPath = transfers.BlockPath;
        info.ProgrammingLanguage = transfers.ProgrammingLanguage;
        info.SupervisionCount = transfers.SupervisionCount;
        info.TextListNames = transfers.TextListNames;
        info.Cultures = transfers.Cultures.ToList();
        info.ExportOption = transfers.ExportOption;
    }

    private static void Describe(PlcAlarmWriteResultInfo result, PlcAlarmTransfers transfers)
    {
        result.PlcName = transfers.Plc?.Software.Name;
        result.DeviceName = transfers.Plc?.DeviceName;
        result.BlockPath = transfers.BlockPath;
        result.TextListNames = transfers.TextListNames;
        result.Cultures = transfers.Cultures.Count > 0 ? transfers.Cultures.ToList() : null;
        result.ExportOption = transfers.ExportOption;
        result.ImportOption = transfers.ImportOption;
    }

    /// <summary>Ends the item's confirmation scope and copies what it answered into the result.</summary>
    private static void Closed(PlcAlarmWriteResultInfo result, PlcDownloadPortalConfirmationLog? confirmations)
        => (confirmations ?? new PlcDownloadPortalConfirmationLog()).CloseInto((entries, observed, complete) =>
        {
            result.PortalConfirmations = entries;
            result.PortalConfirmationsObserved = observed;
            result.PortalConfirmationsComplete = complete;
        });

    private static WorkerOperationException Failure(string category, string message, IReadOnlyList<string>? warnings,
        EngineeringErrorInfo? engineeringError, PlcAlarmWriteResultInfo evidence, PlcDownloadPortalConfirmationLog? confirmations)
    {
        Closed(evidence, confirmations);
        return new(category, message, warnings, engineeringError: engineeringError, plcDownloadOutcome: WorkerJson.SerializePayload(evidence));
    }

    private static string Applied(PlcAlarmWriteResultInfo result)
        => result.AppliedMembers is { Count: > 0 } applied ? $" Already applied: {string.Join(", ", applied)}." : string.Empty;

    /// <summary>Writes warnings to standard error, which the worker returns as the response's warnings.</summary>
    private static void Emit(IEnumerable<string> warnings)
    {
        foreach (var warning in warnings.Distinct(StringComparer.Ordinal)) Console.Error.WriteLine(warning);
    }

    private static PlcProjectLanguagesInfo Languages(ProjectBase project)
    {
        var info = new PlcProjectLanguagesInfo();
        try
        {
            foreach (Language language in project.LanguageSettings.ActiveLanguages) info.Active.Add(language.Culture.Name);
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
        {
            info.Complete = false;
            info.Messages.Add($"The active languages could not be read: {exception.Message}");
        }

        info.Editing = Culture(() => project.LanguageSettings.EditingLanguage, "editing", info);
        info.Reference = Culture(() => project.LanguageSettings.ReferenceLanguage, "reference", info);
        return info;
    }

    private static string? Culture(Func<Language?> read, string what, PlcProjectLanguagesInfo info)
    {
        try
        {
            return read()?.Culture?.Name;
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
        {
            info.Complete = false;
            info.Messages.Add($"The {what} language could not be read: {exception.Message}");
            return null;
        }
    }

    private static bool Room(PlcTextListPlcInfo info)
    {
        if (info.UserTextLists.Count + info.SystemTextLists.Count < PlcTextListResolver.MaxTextLists) return true;
        if (info.Complete) info.Messages.Add($"More than {PlcTextListResolver.MaxTextLists} text lists; the rest is not listed.");
        info.Complete = false;
        return false;
    }

    private static WorkerOperationException Invalid(string message) => new(WorkerFailureCategories.ValidationError, message);
}
