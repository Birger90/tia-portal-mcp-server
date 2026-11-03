using System.Text;
using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcAlarms;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.FakeWorker.Plc;

/// <summary>
/// Stateful <c>plc-alarms-roundtrip</c> fixture for the alarm, alarm class, text list, supervision and ProDiag operations.
/// PLC_1 (Station_1) has the user text lists USER_1 (Decimal) and USER_2 (Bit) and the system text list SystemTextList_1;
/// the active project languages are en-US and de-DE (fr-FR is a catalogue language that is not active). Workbooks are real
/// XLSX packages (<see cref="XlsxFixture"/>, an invented layout); every export, import, cell update and verification runs
/// through the production <see cref="PlcAlarmWorkbookRun"/>, whose delegates here copy the stored workbook to and from the
/// file (an import replaces the stored workbook, as if TIA Portal applied it). Fake enum members (ListRange
/// Decimal/Bit/Binary, export options OptionA/OptionB) are stand-ins, not TIA Portal's names. ProDiag FB: PLC_1/ProDiag_FB;
/// PLC_1/Main is an FB without supervisions; PLC_1/ProDiag_IDB, member Valve_1 of PLC_1/Data and tag Tags/Valve_1 carry
/// AssignedProDiagFB. Triggers: a cell value "SIDE_EFFECT" makes the fake import also change the first sheet's A1, a cell
/// value "KEPT_BY_IMPORT" is replaced by "existing value" on import (an entry the import kept); a file path
/// containing "with-dialog" raises one TIA Portal confirmation answered by the item's portalConfirmations.
/// </summary>
sealed class PlcAlarmScenario
{
    public const string Plc = "PLC_1";
    public const string Device = "Station_1";
    public const string ProDiagFb = "PLC_1/ProDiag_FB";

    private static readonly string[] ListRanges = { "Decimal", "Bit", "Binary" };
    private static readonly string[] ExportOptions = { "OptionA", "OptionB" };
    private static readonly string[] Cultures = { "en-US", "de-DE" };
    private static readonly string[] Catalogue = { "en-US", "de-DE", "fr-FR" };

    private sealed class UserList
    {
        public string Name { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public string ListRange { get; set; } = string.Empty;
        public Dictionary<string, string> Comment { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly List<UserList> _lists = new()
    {
        new UserList { Name = "USER_1", Id = "1", ListRange = "Decimal", Comment = { ["en-US"] = "Motor states", ["de-DE"] = "Motorzustaende" } },
        new UserList { Name = "USER_2", Id = "2", ListRange = "Bit", Comment = { ["en-US"] = "Valve", ["de-DE"] = "Ventil" } },
    };

    private readonly Dictionary<string, string?> _assignments = new(StringComparer.OrdinalIgnoreCase)
    {
        ["instanceDb:PLC_1/ProDiag_IDB"] = null,
        ["dbMember:PLC_1/Data.Valve_1"] = null,
        ["tag:/Tags/Valve_1"] = null,
        // Its AssignedProDiagFB reads back as neither a name nor a block (the worker reports it unreadable).
        ["tag:/Tags/Opaque_1"] = null,
    };

    private const string OpaqueMessage = "GetAttribute(\"AssignedProDiagFB\") returned a System.Object, neither the FB name nor a block; its FB name is not read.";

    private static bool Opaque(string key) => key.EndsWith("/Opaque_1", StringComparison.OrdinalIgnoreCase);

    private byte[] _textLists = XlsxFixture.Build(new[]
    {
        new XlsxFixture.Sheet("Text lists", new[]
        {
            Row("Text list", "Range from", "Range to", "en-US", "de-DE"),
            Row("USER_1", 0, 0, "Stopped", "Gestoppt"),
            Row("USER_1", 1, 1, "Running", "Laeuft"),
            Row("USER_2", 0, 0, "Closed", "Zu"),
        }),
    });

    private byte[] _alarmTexts = XlsxFixture.Build(new[]
    {
        new XlsxFixture.Sheet("Alarm texts", new[] { Row("Alarm", "en-US", "de-DE"), Row("Main.Alarm_1", "Motor overload", "Motorueberlast") }),
    }, fileContent: "Alarm texts");

    private byte[] _supervisions = XlsxFixture.Build(new[]
    {
        new XlsxFixture.Sheet("Supervisions", new[] { Row("Supervision", "Text en-US"), Row("Valve_1.Opened", "Valve does not open") }),
    }, fileContent: "Supervisions");

    private byte[] _alarmClasses = Encoding.UTF8.GetBytes("FAKE ALARM CLASSES\n");
    // The system diagnostics settings: an opaque file like the alarm classes; their import answers Warning (issue #19).
    private byte[] _systemDiagnostics = Encoding.UTF8.GetBytes("FAKE SYSTEM DIAGNOSTICS SETTINGS\n");

    private static IReadOnlyList<object?> Row(params object?[] cells) => cells;

    public string Handle(string line)
    {
        var request = JsonSerializer.Deserialize<WorkerRequest>(line, WorkerJson.Envelope)!;
        var warnings = new List<string>();
        try
        {
            if (request.Method is "read_hardware_config") return Ok(new HardwareConfigInfo(), warnings);
            if (request.Method is "list_tag_tables")
                return Ok(new PlcTagInventoryInfo { Plcs = { new PlcTagInventoryPlcInfo { PlcName = Plc, DeviceName = Device } } }, warnings);
            if (!PlcAlarmMethods.IsWorkerMethod(request.Method))
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"plc-alarms scenario does not script '{request.Method}'.");
            var alarm = request.PlcAlarm ?? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"{request.Method} requires the PLC alarm request.");
            if (request.Method == PlcAlarmMethods.Plan) return Ok(Plan(alarm, warnings), warnings);
            if (PlcAlarmMethods.IsWriteMethod(request.Method)) return Write(request.Method, alarm, warnings);
            RequirePlc(request.Method, alarm.PlcName);
            return request.Method switch
            {
                PlcAlarmMethods.ListTextLists => Ok(Inventory(), warnings),
                PlcAlarmMethods.GetProDiagFb => Ok(Fb(alarm.BlockPath), warnings),
                PlcAlarmMethods.GetProDiagAssignment => Ok(Assignment(alarm), warnings),
                _ => Ok(ReadWorkbook(request.Method, alarm, warnings), warnings),
            };
        }
        catch (PlcAlarmException ex)
        {
            return Failure(ex.Category, ex.Message, warnings, null);
        }
        catch (WorkerOperationException ex)
        {
            return Failure(ex.FailureCategory, ex.Message, warnings, ex.PlcDownloadOutcome);
        }
    }

    // ================================================================ reads

    private PlcTextListInventoryInfo Inventory() => new()
    {
        ProjectLanguages = new PlcProjectLanguagesInfo { Active = Cultures.ToList(), Editing = "en-US", Reference = "en-US" },
        Plcs =
        {
            new PlcTextListPlcInfo
            {
                PlcName = Plc,
                DeviceName = Device,
                UserTextLists = _lists.Select(list => new PlcTextListInfo
                {
                    Name = list.Name, Id = list.Id, ListRange = list.ListRange,
                    Comment = list.Comment.OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase).Select(c => new PlcAlarmTextItemInfo { Culture = c.Key, Text = c.Value }).ToList(),
                }).ToList(),
                SystemTextLists = { new PlcTextListInfo { Name = "SystemTextList_1", IsSystem = true, Id = "1001", ListRange = "Decimal" } },
            },
        },
    };

    private PlcAlarmWorkbookInfo ReadWorkbook(string method, PlcAlarmRequestInfo request, List<string> warnings)
    {
        var info = new PlcAlarmWorkbookInfo { Kind = PlcAlarmMethods.WorkbookKind(method)! };
        var kind = Resolve(method, request, warnings);
        Describe(info, kind);
        var run = new PlcAlarmWorkbookRun();
        try
        {
            run.Read(Exporter(kind.Name), info, new XlsxProjection { Sheet = request.Sheet, StartRow = request.StartRow, StartColumn = request.StartColumn is null ? 1 : PlcAlarmPolicy.ParseColumn(request.StartColumn) ?? 0, MaxRows = request.MaxRows, MaxChars = request.MaxResultChars },
                request.Probes);
        }
        finally
        {
            warnings.AddRange(run.Warnings);
        }

        return info;
    }

    private static PlcProDiagFbInfo Fb(string? blockPath)
    {
        var block = Block(blockPath);
        return new PlcProDiagFbInfo
        {
            BlockPath = blockPath!,
            PlcName = Plc,
            BlockType = "FB",
            ProgrammingLanguage = block == ProDiagFb ? "ProDiag" : "SCL",
            IsProDiag = block == ProDiagFb,
            SupervisionCount = block == ProDiagFb ? 1 : 0,
            IsConsistent = true,
            HasSupervisionProvider = block == ProDiagFb,
            Attributes = block == ProDiagFb
                ? new List<PlcProDiagAttributeInfo> { new() { Name = "ProDiagVersion", Value = "2.0" }, new() { Name = "InitialValueAcquisition", Value = "False" }, new() { Name = "UseCentralTimeStamp", Value = "True" } }
                : new List<PlcProDiagAttributeInfo>(),
        };
    }

    private PlcProDiagAssignmentInfo Assignment(PlcAlarmRequestInfo request)
    {
        var (key, kind, display) = Target(request);
        return Opaque(key)
            ? new PlcProDiagAssignmentInfo { TargetKind = kind, Target = display, PlcName = Plc, Readable = false, Message = OpaqueMessage }
            : new PlcProDiagAssignmentInfo { TargetKind = kind, Target = display, PlcName = Plc, AssignedProDiagFb = _assignments[key], Readable = true };
    }

    // ================================================================ planning

    private PlcAlarmPlanInfo Plan(PlcAlarmRequestInfo request, List<string> warnings)
    {
        var method = request.PlannedOperation;
        if (!PlcAlarmMethods.IsWriteMethod(method)) throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"plc_alarm_plan plans a plc_write alarm operation, not '{method}'.");
        var plan = new PlcAlarmPlanInfo { Operation = method!, Bound = { $"fake binding of {method}" } };
        switch (method)
        {
            case PlcAlarmMethods.UpdateTextList:
                PrepareUpdate(request);
                (plan.PlcName, plan.DeviceName) = (Plc, Device);
                break;
            case PlcAlarmMethods.DeleteTextList:
                _ = User(request.TextListName);
                (plan.PlcName, plan.DeviceName) = (Plc, Device);
                break;
            case PlcAlarmMethods.SetProDiagAssignment:
            {
                plan.PlcName = Plc;
                var current = Assignment(request);
                plan.AssignedProDiagFb = current.AssignedProDiagFb;
                if (!current.Readable) warnings.Add($"{current.Target}: the current assignment is treated as unset; {current.Message}");
                break;
            }
            case PlcAlarmMethods.ExportProDiagInfo:
                _ = ProDiag(request.BlockPath);
                plan.BlockPath = request.BlockPath;
                (plan.ProgrammingLanguage, plan.SupervisionCount) = ("ProDiag", 1);
                if (PlcAlarmPolicy.CheckDirectoryPath(request.DirectoryPath) is { } invalidDirectory) throw new WorkerOperationException(WorkerFailureCategories.ValidationError, invalidDirectory);
                plan.Path = PlcAlarmFiles.Inspect(request.DirectoryPath!);
                break;
            default:
            {
                var kind = Resolve(method!, request, warnings);
                (plan.PlcName, plan.DeviceName, plan.BlockPath, plan.TextListNames) = (kind.PlcName, kind.PlcName is null ? null : Device, kind.BlockPath, kind.TextListNames);
                (plan.ProgrammingLanguage, plan.SupervisionCount) = kind.BlockPath is null ? (null, null) : ("ProDiag", (int?)1);
                if (request.FilePath is not null)
                {
                    if (PlcAlarmPolicy.CheckFilePath(request.FilePath, PlcAlarmPolicy.RequiredExtension(method)) is { } invalid)
                        throw new WorkerOperationException(WorkerFailureCategories.ValidationError, invalid);
                    plan.Path = PlcAlarmFiles.Inspect(request.FilePath);
                }

                break;
            }
        }

        plan.Messages.AddRange(warnings);
        return plan;
    }

    // ================================================================ writes

    /// <summary>Like the worker's Write: the result is the evidence of every exit, with the confirmation record.</summary>
    private string Write(string method, PlcAlarmRequestInfo request, List<string> warnings)
    {
        var policy = request.PortalConfirmations ?? PlcDownloadPolicy.PortalConfirmationPolicies.Decline;
        if (!PlcDownloadPolicy.PortalConfirmationPolicies.IsKnown(policy))
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"portalConfirmations must be one of: {string.Join(", ", PlcDownloadPolicy.PortalConfirmationPolicies.All)}.");
        var result = new PlcAlarmWriteResultInfo { Operation = method, PortalConfirmationPolicy = policy };
        var run = new PlcAlarmWorkbookRun();
        try
        {
            Mutate(method, request, result, run, warnings);
        }
        catch (PlcAlarmException ex)
        {
            warnings.AddRange(run.Warnings);
            return Failure(ex.Category, ex.Message, warnings, WorkerJson.SerializePayload(result));
        }
        catch (WorkerOperationException ex)
        {
            warnings.AddRange(run.Warnings);
            return Failure(ex.FailureCategory, ex.Message, warnings, WorkerJson.SerializePayload(result));
        }

        warnings.AddRange(run.Warnings);
        if ((request.FilePath ?? request.DirectoryPath)?.Contains("with-dialog", StringComparison.Ordinal) == true)
        {
            result.PortalConfirmationsObserved = 1;
            result.PortalConfirmations.Add(new PlcDownloadPortalConfirmationInfo
            {
                Type = "Siemens.Engineering.ConfirmationEventArgs", Text = "Fake dialog: replace existing data?", Answer = PlcDownloadPolicy.PortalConfirmationPolicies.AnswerFor(policy),
            });
        }

        return Ok(result, warnings);
    }

    private void Mutate(string method, PlcAlarmRequestInfo request, PlcAlarmWriteResultInfo result, PlcAlarmWorkbookRun run, List<string> warnings)
    {
        switch (method)
        {
            case PlcAlarmMethods.UpdateTextList:
                UpdateTextList(request, result);
                return;
            case PlcAlarmMethods.DeleteTextList:
            {
                var list = User(request.TextListName);
                _lists.Remove(list);
                (result.PlcName, result.DeviceName, result.TextListName) = (Plc, Device, list.Name);
                return;
            }
            case PlcAlarmMethods.SetProDiagAssignment:
            {
                var (key, kind, display) = Target(request);
                if (!Same(request.ProDiagFb, "ProDiag_FB"))
                    throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Block '{request.ProDiagFb}' was not found in PLC '{Plc}'.");
                (result.PlcName, result.TargetKind, result.Target, result.AssignedProDiagFbBefore) = (Plc, kind, display, _assignments[key]);
                _assignments[key] = "ProDiag_FB";
                result.AppliedMembers = new List<string> { PlcAlarmPolicy.AssignedProDiagFbAttribute };
                if (Opaque(key))
                {
                    result.AssignedProDiagFbBefore = null;
                    warnings.Add($"{display}: the assignment before the write is treated as unset; {OpaqueMessage}");
                    warnings.Add($"{display}: SetAttribute(\"AssignedProDiagFB\", 'ProDiag_FB') returned, but the value does not read back: {OpaqueMessage}");
                    return;
                }

                result.AssignedProDiagFbAfter = _assignments[key];
                return;
            }
            case PlcAlarmMethods.ExportProDiagInfo:
                _ = ProDiag(request.BlockPath);
                result.BlockPath = request.BlockPath;
                result.IsConsistent = true;
                run.ExportDirectory(directory =>
                {
                    File.WriteAllText(Path.Combine(directory, "ProDiag_FB_alarms.csv"), "ClientAlarmId;SupervisionId;Priority;Category\n1;0x1;4;Operation\n");
                    File.WriteAllText(Path.Combine(directory, "ProDiag_FB_supervisions.csv"), "SupervisionId;Text\n0x1;Valve does not open\n");
                }, request.DirectoryPath, result);
                return;
        }

        var kindOf = Resolve(method, request, warnings);
        Describe(result, kindOf);
        var export = Exporter(kindOf.Name);
        var import = Importer(kindOf.Name);
        if (PlcAlarmMethods.CellUpdates.Contains(method))
        {
            run.UpdateCells(export, import, request.CellEdits, request.ExpectedWorkbookHash, PlcAlarmMethods.WorkbookReadFor(method)!, result);
            return;
        }

        var classes = PlcAlarmPolicy.WorkbookKinds.IsOpaqueFile(kindOf.Name);
        if (PlcAlarmMethods.FileExports.Contains(method))
        {
            run.ExportFile(export, request.FilePath, request.Overwrite, PlcAlarmPolicy.RequiredExtension(method), classes, result);
            return;
        }

        var reexport = method switch
        {
            PlcAlarmMethods.ImportSupervisionSettings => null,
            PlcAlarmMethods.ImportAlarmTexts when request.ExportOption is null => null,
            _ => export,
        };
        var reason = method == PlcAlarmMethods.ImportSupervisionSettings ? "no documented export reads supervision settings back (only ExportSupervisionsToXlsx exists)."
            : reexport is null ? "the request names no exportOption to re-export the alarm texts with." : null;
        run.ImportFile(import, reexport, request.FilePath, PlcAlarmPolicy.RequiredExtension(method), classes, classes, reason, result);
    }

    private void UpdateTextList(PlcAlarmRequestInfo request, PlcAlarmWriteResultInfo result)
    {
        var list = PrepareUpdate(request);
        (result.PlcName, result.DeviceName, result.TextListName) = (Plc, Device, list.Name);
        result.AppliedMembers = new List<string>();
        if (request.NewName is not null)
        {
            list.Name = request.NewName;
            result.AppliedMembers.Add("Name");
        }

        if (request.ListRange is not null)
        {
            list.ListRange = request.ListRange;
            result.AppliedMembers.Add("ListRange");
        }

        if (request.CommentCulture is not null)
        {
            list.Comment[request.CommentCulture] = request.Comment ?? string.Empty;
            result.AppliedMembers.Add($"Comment[{request.CommentCulture}]");
        }

        result.TextListAfter = Inventory().Plcs[0].UserTextLists.Single(l => l.Name == list.Name);
    }

    private UserList PrepareUpdate(PlcAlarmRequestInfo request)
    {
        var list = User(request.TextListName);
        if (request.NewName is not null && (_lists.Any(other => other != list && Same(other.Name, request.NewName)) || Same(request.NewName, "SystemTextList_1")))
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"User text list '{list.Name}' cannot be renamed to '{request.NewName}': the name is taken. Nothing was changed.");
        if (request.ListRange is not null && !ListRanges.Contains(request.ListRange, StringComparer.Ordinal))
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                $"'{request.ListRange}' is not a PlcAlarmTextlistListRange member on this TIA Portal installation (members: {string.Join(", ", ListRanges)}). Nothing was changed.");
        if (request.CommentCulture is not null && !list.Comment.ContainsKey(request.CommentCulture))
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"The comment has no text item for culture '{request.CommentCulture}'. Nothing was changed.");
        return list;
    }

    // ================================================================ workbook kinds

    private sealed record Resolved(string Name, string? PlcName, string? BlockPath, List<string>? TextListNames, List<string> Cultures, string? ExportOption, string? ImportOption);

    /// <summary>The checks the worker's PlcAlarmTransfers makes before anything is exported or imported.</summary>
    private Resolved Resolve(string method, PlcAlarmRequestInfo request, List<string> warnings)
    {
        var kind = PlcAlarmMethods.WorkbookKind(method)!;
        var importOption = PlcAlarmMethods.CellUpdates.Contains(method) || PlcAlarmMethods.FileImports.Contains(method) ? request.ImportOption ?? PlcAlarmPolicy.DefaultImportOption : null;
        switch (kind)
        {
            case PlcAlarmPolicy.WorkbookKinds.TextLists:
            {
                RequirePlc(method, request.PlcName);
                var cellUpdate = method == PlcAlarmMethods.UpdateTextListCells;
                if (cellUpdate && request.TextListNames is null) throw new WorkerOperationException(WorkerFailureCategories.ValidationError, PlcAlarmPolicy.CellUpdatesNeedTextListNames);
                var filtered = cellUpdate || method != PlcAlarmMethods.ImportTextLists && (request.TextListNames is not null || request.Cultures is not null);
                if (!filtered) return new Resolved(kind, Plc, null, null, new List<string>(), null, importOption);
                if (request.TextListNames is null) throw new WorkerOperationException(WorkerFailureCategories.ValidationError, PlcAlarmPolicy.CulturesNeedTextListNames);
                var names = request.TextListNames.Select(name =>
                {
                    if (_lists.FirstOrDefault(l => Same(l.Name, name)) is { } user) return user.Name;
                    if (!Same(name, "SystemTextList_1"))
                        throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Text list '{name}' was not found in PLC '{Plc}'. Nothing was exported.");
                    if (method == PlcAlarmMethods.UpdateTextListCells)
                        throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                            $"'SystemTextList_1' is a system text list of PLC '{Plc}'; a cell update imports the workbook again, and system text lists are not changed through this server. Nothing was exported.");
                    return "SystemTextList_1";
                }).ToList();
                return new Resolved(kind, Plc, null, names, Languages(request.Cultures, warnings), null, importOption);
            }
            case PlcAlarmPolicy.WorkbookKinds.AlarmTexts:
            {
                RequirePlc(method, request.PlcName);
                var exports = method != PlcAlarmMethods.ImportAlarmTexts || request.ExportOption is not null;
                if (exports && !ExportOptions.Contains(request.ExportOption, StringComparer.Ordinal))
                    throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                        $"exportOption '{request.ExportOption}' is not a PlcAlarmTextXlsxExportOption member on this TIA Portal installation (members: {string.Join(", ", ExportOptions)}). Nothing was exported.");
                return new Resolved(kind, Plc, null, null, Languages(request.Cultures, warnings), exports ? request.ExportOption : null, null);
            }
            case PlcAlarmPolicy.WorkbookKinds.AlarmClasses:
            case PlcAlarmPolicy.WorkbookKinds.SystemDiagnosticsSettings:
                return new Resolved(kind, null, null, null, new List<string>(), null, null);
            default:
                return new Resolved(kind, null, ProDiag(request.BlockPath), null, new List<string>(), null, importOption);
        }
    }

    private PlcAlarmWorkbookRun.Transfer Exporter(string kind) => path =>
    {
        File.WriteAllBytes(path, Stored(kind));
        return new PlcAlarmResultReader.Outcome { RuntimeType = "Fake.Result", State = "Success", LogFilePath = @"C:\FakeWorker\Logs\export.log" };
    };

    private PlcAlarmWorkbookRun.Transfer Importer(string kind) => path =>
    {
        var bytes = File.ReadAllBytes(path);
        if (!PlcAlarmPolicy.WorkbookKinds.IsOpaqueFile(kind))
        {
            XlsxWorkbookGrid grid;
            try
            {
                grid = XlsxCellWorkbook.Read(bytes);
            }
            catch (PlcAlarmException)
            {
                return new PlcAlarmResultReader.Outcome { RuntimeType = "Fake.Result", State = "Failure", LogFilePath = @"C:\FakeWorker\Logs\import.log" };
            }

            // The SIDE_EFFECT trigger: the fake import also changes a cell nobody edited.
            if (grid.Sheets.SelectMany(s => s.Cells).Any(c => c.Value == "SIDE_EFFECT"))
                bytes = XlsxCellWorkbook.Patch(bytes, new[] { new PlcAlarmCellEditInfo { Sheet = grid.Sheets[0].Name, Cell = "A1", Expected = XlsxCellWorkbook.Probe(grid, grid.Sheets[0].Name, "A1").Value, Value = "changed by import" } }).Workbook;
            // The KEPT_BY_IMPORT trigger: the fake import keeps the existing value of that cell (like ImportOptions.None).
            var kept = grid.Sheets.SelectMany(sheet => sheet.Cells.Where(c => c.Value == "KEPT_BY_IMPORT").Select(c => new PlcAlarmCellEditInfo { Sheet = sheet.Name, Cell = c.Reference, Expected = c.Value, Value = "existing value" })).ToList();
            if (kept.Count > 0) bytes = XlsxCellWorkbook.Patch(bytes, kept).Workbook;
        }

        if (kind != PlcAlarmPolicy.WorkbookKinds.SupervisionSettings) Store(kind, bytes);
        if (kind == PlcAlarmPolicy.WorkbookKinds.SystemDiagnosticsSettings)
            return new PlcAlarmResultReader.Outcome { RuntimeType = "Fake.SystemdiagnosticsSettingsExportImportResult", State = "Warning" };
        return new PlcAlarmResultReader.Outcome { RuntimeType = "Fake.Result", State = "Success", LogFilePath = @"C:\FakeWorker\Logs\import.log", ErrorCount = 0, WarningCount = 0, Messages = new List<string>() };
    };

    private byte[] Stored(string kind) => kind switch
    {
        PlcAlarmPolicy.WorkbookKinds.TextLists => _textLists,
        PlcAlarmPolicy.WorkbookKinds.AlarmTexts => _alarmTexts,
        PlcAlarmPolicy.WorkbookKinds.AlarmClasses => _alarmClasses,
        PlcAlarmPolicy.WorkbookKinds.SystemDiagnosticsSettings => _systemDiagnostics,
        _ => _supervisions,
    };

    private void Store(string kind, byte[] bytes)
    {
        switch (kind)
        {
            case PlcAlarmPolicy.WorkbookKinds.TextLists:
                _textLists = bytes;
                break;
            case PlcAlarmPolicy.WorkbookKinds.AlarmTexts:
                _alarmTexts = bytes;
                break;
            case PlcAlarmPolicy.WorkbookKinds.AlarmClasses:
                _alarmClasses = bytes;
                break;
            case PlcAlarmPolicy.WorkbookKinds.SystemDiagnosticsSettings:
                _systemDiagnostics = bytes;
                break;
            default:
                _supervisions = bytes;
                break;
        }
    }

    private static void Describe(PlcAlarmWorkbookInfo info, Resolved kind)
    {
        (info.PlcName, info.DeviceName, info.BlockPath) = (kind.PlcName, kind.PlcName is null ? null : Device, kind.BlockPath);
        (info.TextListNames, info.Cultures, info.ExportOption) = (kind.TextListNames, kind.Cultures.ToList(), kind.ExportOption);
        if (kind.BlockPath is not null) (info.ProgrammingLanguage, info.SupervisionCount) = ("ProDiag", 1);
    }

    private static void Describe(PlcAlarmWriteResultInfo result, Resolved kind)
    {
        (result.PlcName, result.DeviceName, result.BlockPath) = (kind.PlcName, kind.PlcName is null ? null : Device, kind.BlockPath);
        (result.TextListNames, result.Cultures) = (kind.TextListNames, kind.Cultures.Count > 0 ? kind.Cultures.ToList() : null);
        (result.ExportOption, result.ImportOption) = (kind.ExportOption, kind.ImportOption);
    }

    // ================================================================ helpers

    private UserList User(string? name)
    {
        var list = _lists.FirstOrDefault(l => Same(l.Name, name));
        if (list is not null) return list;
        if (Same(name, "SystemTextList_1"))
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                $"'{name}' is a system text list of PLC '{Plc}'; system text lists are not changed through this server. Nothing was changed.");
        throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"User text list '{name}' was not found in PLC '{Plc}'.");
    }

    private static List<string> Languages(IReadOnlyList<string>? cultures, List<string> warnings)
    {
        foreach (var culture in cultures ?? Array.Empty<string>())
        {
            if (!Catalogue.Contains(culture, StringComparer.OrdinalIgnoreCase))
                throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                    $"'{culture}' is not a TIA Portal language (LanguageSettings.Languages); the active project languages are {string.Join(", ", Cultures)}. Nothing was exported or imported.");
            if (!Cultures.Contains(culture, StringComparer.OrdinalIgnoreCase))
                warnings.Add($"'{culture}' is not an active project language; TIA Portal decides what the export or import does with it.");
        }

        return (cultures ?? Cultures).Select(c => Catalogue.First(known => Same(known, c))).ToList();
    }

    private static string ProDiag(string? blockPath)
    {
        var block = Block(blockPath);
        if (block == ProDiagFb) return blockPath!;
        throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
            $"FB '{blockPath}' offers no SupervisionProvider (it is not a ProDiag FB: ProgrammingLanguage SCL, FB.Supervisions.Count 0). Nothing was changed.");
    }

    private static string Block(string? blockPath)
        => Same(blockPath, ProDiagFb) ? ProDiagFb : Same(blockPath, "PLC_1/Main") ? "PLC_1/Main"
            : throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Block '{blockPath}' was not found.");

    private (string Key, string Kind, string Display) Target(PlcAlarmRequestInfo request)
    {
        var (key, kind, display) = request.TableName is not null
            ? ($"tag:{(request.FolderPath ?? "/").TrimEnd('/')}/{request.TableName}/{request.Name}", PlcAlarmPolicy.AssignmentTargets.Tag, $"Tag '{request.Name}' of tag table '{request.TableName}'")
            : request.MemberPath is not null
                ? ($"dbMember:{request.BlockPath}.{request.MemberPath}", PlcAlarmPolicy.AssignmentTargets.DbMember, $"Member '{request.MemberPath}' of '{request.BlockPath}'")
                : ($"instanceDb:{request.BlockPath}", PlcAlarmPolicy.AssignmentTargets.InstanceDb, $"Block '{request.BlockPath}'");
        if (!_assignments.ContainsKey(key)) throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"{display} was not found.");
        return (key, kind, display);
    }

    private static void RequirePlc(string method, string? plcName)
    {
        if (PlcAlarmMethods.IsProjectScoped(method) || PlcAlarmMethods.UsesBlockPath(method)) return;
        if (plcName is not null && !Same(plcName, Plc) && !Same(plcName, Device))
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No PLC software named '{plcName}' was found in the project.");
    }

    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string Ok<T>(T payload, List<string> warnings)
        => JsonSerializer.Serialize(new WorkerResponse
        {
            Success = true,
            Payload = WorkerJson.SerializePayload(payload),
            Warnings = warnings.Count > 0 ? warnings.Distinct(StringComparer.Ordinal).ToList() : null,
        }, WorkerJson.Envelope);

    private static string Failure(string category, string message, List<string> warnings, string? evidence)
        => JsonSerializer.Serialize(new WorkerResponse
        {
            Success = false,
            FailureCategory = category,
            Error = message,
            PlcDownloadOutcome = evidence,
            Warnings = warnings.Count > 0 ? warnings.Distinct(StringComparer.Ordinal).ToList() : null,
        }, WorkerJson.Envelope);
}
