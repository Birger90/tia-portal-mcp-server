using System.Text.Json;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.CrossReferences;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.FakeWorker.Hmi;

/// <summary>Stateful synthetic HMI writes and public read-back; no Siemens objects are loaded.</summary>
internal sealed class HmiWriteRoundtripScenario
{
    private readonly List<string> _tables = ["Existing"];
    private readonly List<HmiTagRowInfo> _tags = [];
    private readonly List<HmiAlarmRowInfo> _alarms = [];
    /// <summary>Screens by name with their items (name, type) and one script per screen (adopted screen writes).</summary>
    private readonly Dictionary<string, (List<(string Name, string Type)> Items, string Script)> _screens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Main"] = (new() { ("Button_1", "HmiButton") }, "HmiRuntime.UI.SysFct.ChangeScreen('Detail');"),
    };
    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static string Ok(object value) => JsonSerializer.Serialize(new WorkerResponse { Success = true, Payload = WorkerJson.SerializePayload(value) }, WorkerJson.Envelope);
    /// <summary>Portal's duplicate-create rejections captured through the installed tool (V21, 2026-10-30: P1 table, P4 tag, P8/P9 alarms).</summary>
    internal const string NativeDuplicateTableMessage = "Error when calling method 'Create' of type 'Siemens.Engineering.HmiUnified.HmiTags.HmiTagTableComposition'.\r\n\r\n-The value must be unique.\r\n";
    internal const string NativeDuplicateTagMessage = "Error when calling method 'Create' of type 'Siemens.Engineering.HmiUnified.HmiTags.HmiTagComposition'.\r\n\r\n-The value must be unique.\r\n";
    internal const string NativeDuplicateDiscreteAlarmMessage = "Error when calling method 'Create' of type 'Siemens.Engineering.HmiUnified.HmiAlarm.HmiDiscreteAlarmComposition'.\r\n\r\n-The value must be unique.\r\n";
    internal const string NativeDuplicateAnalogAlarmMessage = "Error when calling method 'Create' of type 'Siemens.Engineering.HmiUnified.HmiAlarm.HmiAnalogAlarmComposition'.\r\n\r\n-The value must be unique.\r\n";
    /// <summary>Portal's DataType rejection on a PLC-bound tag captured through the installed tool (2026-10-30, P15-update).</summary>
    internal const string NativeBoundDataTypeMessage = "Error when calling method 'set_DataType' of type 'Siemens.Engineering.HmiUnified.HmiTags.HmiTag'.\r\n\r\n-Set is not allowed for disabled fields\r\n";
    private static string Rejected(string message, HmiWriteOutcomeInfo outcome) => JsonSerializer.Serialize(new WorkerResponse
    {
        Success = false, FailureCategory = "engineering_rejected", Error = "TIA Portal rejected the operation: " + message, HmiWriteOutcome = outcome,
        EngineeringError = new() { Type = "EngineeringTargetInvocationException", Message = message },
    }, WorkerJson.Envelope);
    private string? Duplicate(string method, HmiWriteRequestInfo w, HmiWriteOutcomeInfo outcome)
    {
        var exists = method switch
        {
            "hmi_create_tag_table" => _tables.Any(t => Same(t, w.TableName)),
            "hmi_create_tag" => _tags.Any(t => Same(t.Name, w.TagName)),
            "hmi_create_alarm" => _alarms.Any(a => Same(a.Name, w.AlarmName) && a.Kind == w.AlarmKind),
            _ => false,
        };
        if (!exists) return null;
        outcome.FailedProperty = "Create"; outcome.Cleanup = HmiCleanupStates.NotNeeded;
        return Rejected(method switch
        {
            "hmi_create_tag_table" => NativeDuplicateTableMessage,
            "hmi_create_tag" => NativeDuplicateTagMessage,
            _ => w.AlarmKind == "analog" ? NativeDuplicateAnalogAlarmMessage : NativeDuplicateDiscreteAlarmMessage,
        }, outcome);
    }
    private static string Missing() => JsonSerializer.Serialize(new WorkerResponse { Success = false, FailureCategory = "target_not_found", Error = "Synthetic object not found." }, WorkerJson.Envelope);

    public string Handle(string line)
    {
        var r = JsonSerializer.Deserialize<WorkerRequest>(line, WorkerJson.Envelope)!;
        var q = r.HmiQuery;
        switch (r.Method)
        {
            case "read_hardware_config": return Ok(new HardwareConfigInfo());
            case "read_cross_references": return Ok(new CrossReferenceReport { IsComplete = true, OwnerQueryCount = 1, SuccessfulOwnerQueryCount = 1, Target = r.CrossReferenceSelector! });
            case "hmi_list_hmi_devices": return Ok(new HmiDeviceListInfo { Devices = [new() { SoftwareName = "H", DeviceName = "Device", Kind = "unified" }, new() { SoftwareName = "C", DeviceName = "Panel", Kind = "classic" }] });
            case HmiClassicMethods.ListObjects: return ClassicList(q!);
            case HmiScreenEventMethods.ListEvents:
            case HmiScreenEventMethods.ListDynamizations: return EventsRead(r.Method, q!);
            case HmiUnifiedConfigurationMethods.ListPlantViews: return PlantViews(q!);
            case "hmi_list_project_languages": return Ok(new HmiProjectLanguagesInfo { Languages = ["en-US", "pl-PL"] });
            case "hmi_list_tag_tables": return Ok(new HmiTagTableTreeInfo { Tables = _tables.Select(t => new HmiTagTableInfo { Name = t, TagCount = _tags.Count(tag => Same(tag.Table, t)) }).ToList() });
            case "hmi_list_tags": return Ok(new HmiTagListInfo { Tags = _tags.Where(t => q?.TableName is null || Same(t.Table, q.TableName)).ToList(), Page = new(0, 2000, _tags.Count(t => q?.TableName is null || Same(t.Table, q.TableName)), null) });
            case "hmi_list_connections": return Ok(new HmiConnectionListInfo { Connections = [new() { Name = "Connection" }] });
            case "hmi_list_alarm_classes": return Ok(new HmiAlarmClassesInfo { AlarmClasses = [new() { Name = "Alarm" }] });
            case "hmi_list_alarms": return Ok(new HmiAlarmListInfo { Alarms = _alarms.ToList(), Page = new(0, 2000, _alarms.Count, null) });
            case "hmi_get_tag": return _tags.FirstOrDefault(t => Same(t.Name, q!.TagName)) is { } tag ? Ok(new HmiTagDetailInfo { Tag = tag }) : Missing();
            case "hmi_get_alarm": return _alarms.FirstOrDefault(a => Same(a.Name, q!.AlarmName) && a.Kind == q.AlarmKind) is { } alarm ? Ok(new HmiAlarmDetailInfo { Alarm = alarm }) : Missing();
            case "hmi_validate": return Ok(new HmiValidationInfo { Page = new(0, 2000, 1, null), Scanned = 1, Clean = 1 });
        }
        var w = r.HmiWrite!;
        if (w is not null && HmiClassicMethods.IsClassic(r.Method)) return Classic(r.Method, w);
        if (w is not null && w.HmiName == "H" && HmiScreenEventMethods.Writes.Contains(r.Method)) return Events(r.Method, w);
        if (w is not null && w.HmiName == "H" && HmiUnifiedConfigurationMethods.Writes.Contains(r.Method)) return Configuration(r.Method, w);
        if (w is null || w.HmiName != "H") return Missing();
        if (r.Method.Contains("screen", StringComparison.Ordinal) || r.Method is "hmi_set_dynamization_tag" or "hmi_replace_in_scripts" or "hmi_rename_tags")
            return Screens(r.Method, w);
        if (r.Method is HmiTextListMethods.ExportTextLists or HmiTextListMethods.ImportTextLists)
            return TextListFiles(r.Method, w);
        var outcome = new HmiWriteOutcomeInfo { ObjectName = w.NewName ?? w.TagName ?? w.AlarmName ?? w.TableName! };
        if (Duplicate(r.Method, w, outcome) is { } duplicate) return duplicate;
        switch (r.Method)
        {
            case "hmi_create_tag_table": _tables.Add(w.TableName!); outcome.AppliedProperties.Add("Name"); break;
            case "hmi_update_tag_table":
                var index = _tables.FindIndex(t => Same(t, w.TableName));
                if (index < 0) return Missing();
                _tables[index] = w.NewName!;
                foreach (var tag in _tags.Where(t => Same(t.Table, w.TableName))) tag.Table = w.NewName;
                outcome.AppliedProperties.Add("Name"); break;
            case "hmi_delete_tag_table": _tables.RemoveAll(t => Same(t, w.TableName)); _tags.RemoveAll(t => Same(t.Table, w.TableName)); break;
            case "hmi_create_tag":
            case "hmi_update_tag":
                var created = r.Method == "hmi_create_tag";
                var row = created ? new HmiTagRowInfo { Name = w.TagName!, Table = w.TableName, Connection = "<Internal tag>" } : _tags.FirstOrDefault(t => Same(t.Name, w.TagName));
                if (row is null) return Missing();
                if (created) { _tags.Add(row); outcome.AppliedProperties.Add("Name"); }
                if (w.NewName is not null)
                {
                    foreach (var alarm in _alarms.Where(a => a.TriggerTag is not null && (Same(a.TriggerTag, row.Name) || a.TriggerTag.StartsWith(row.Name + ".", StringComparison.OrdinalIgnoreCase))))
                        alarm.TriggerTag = w.NewName + alarm.TriggerTag![row.Name.Length..];
                    row.Name = w.NewName; outcome.AppliedProperties.Add("Name");
                }
                if (w.Connection is not null) { row.Connection = w.Connection; outcome.AppliedProperties.Add("Connection"); }
                if (w.PlcTag == "MalformedOutcome")
                {
                    outcome.ObjectName = "";
                    outcome.CleanupMessage = "malformed evidence must not be echoed";
                    return JsonSerializer.Serialize(new WorkerResponse { Success = false, Error = "Synthetic setter failed after create.", FailureCategory = "worker_operation_failed", HmiWriteOutcome = outcome, Warnings = [new string('x', 61000)] }, WorkerJson.Envelope);
                }
                if (w.PlcTag == "PortalLost")
                {
                    outcome.AppliedProperties.Clear(); outcome.AppliedProperties.Add("Create");
                    return JsonSerializer.Serialize(new WorkerResponse { Success = false, Error = "Synthetic portal loss.", FailureCategory = "portal_lost", HmiWriteOutcome = outcome }, WorkerJson.Envelope);
                }
                if (w.PlcTag is "Missing" or "CleanupFails")
                {
                    outcome.FailedProperty = "PlcTag";
                    if (created)
                    {
                        outcome.Cleanup = w.PlcTag == "Missing" ? "deleted" : "failed";
                        if (outcome.Cleanup == "deleted") _tags.Remove(row); else outcome.CleanupMessage = "Synthetic cleanup identity changed.";
                    }
                    return JsonSerializer.Serialize(new WorkerResponse { Success = false, Error = "Synthetic PLC tag rejected.", FailureCategory = "worker_operation_failed", HmiWriteOutcome = outcome }, WorkerJson.Envelope);
                }
                if (w.PlcTag is not null) { row.PlcTag = w.PlcTag; row.PlcName = "PLC"; row.DataType = "Bool"; outcome.AppliedProperties.Add("PlcTag"); }
                if (w.DataType is not null && row.PlcTag is not null)
                {
                    outcome.FailedProperty = "DataType";
                    if (created) { _tags.Remove(row); outcome.Cleanup = HmiCleanupStates.Deleted; } // the worker deletes its own failed create
                    return Rejected(NativeBoundDataTypeMessage, outcome);
                }
                if (w.DataType is not null) { row.DataType = w.DataType; outcome.AppliedProperties.Add("DataType"); }
                if (w.AcquisitionCycle is not null) row.AcquisitionCycle = w.AcquisitionCycle;
                if (w.Comment is not null) row.Comment = w.Comment;
                if (w.DisplayName is not null) row.DisplayName = w.DisplayName;
                break;
            case "hmi_delete_tag": _tags.RemoveAll(t => Same(t.Name, w.TagName)); break;
            case "hmi_create_alarm":
            case "hmi_update_alarm":
                var alarmRow = r.Method == "hmi_create_alarm" ? new HmiAlarmRowInfo { Name = w.AlarmName!, Kind = w.AlarmKind! } : _alarms.FirstOrDefault(a => Same(a.Name, w.AlarmName) && a.Kind == w.AlarmKind);
                if (alarmRow is null) return Missing();
                if (r.Method == "hmi_create_alarm") _alarms.Add(alarmRow);
                if (w.NewName is not null) alarmRow.Name = w.NewName;
                if (w.TriggerTag is not null) alarmRow.TriggerTag = w.TriggerTag;
                if (w.BitNumber is not null) alarmRow.TriggerBit = w.BitNumber;
                if (w.TriggerMode is not null) alarmRow.TriggerMode = w.TriggerMode;
                if (w.Condition is not null) alarmRow.Condition = w.Condition;
                if (w.ConditionValueInt32 is { } integer) alarmRow.ConditionValue = new("System.Int32", JsonSerializer.SerializeToElement(integer));
                if (w.ConditionValueDouble is { } number) alarmRow.ConditionValue = new("System.Double", JsonSerializer.SerializeToElement(number));
                if (w.AlarmClass is not null) alarmRow.AlarmClass = w.AlarmClass;
                if (w.Priority is not null) alarmRow.Priority = w.Priority;
                if (w.EventText is not null) alarmRow.EventText = w.EventText;
                if (w.InfoText is not null) alarmRow.InfoText = w.InfoText;
                outcome.AppliedProperties.Add("Name"); break;
            case "hmi_delete_alarm": _alarms.RemoveAll(a => Same(a.Name, w.AlarmName) && a.Kind == w.AlarmKind); break;
            default: return Missing();
        }
        return Ok(outcome);
    }

    /// <summary>
    /// The screen, script and batch rename writes: a missing screen or item is target_not_found, an itemType other than
    /// HmiButton, HmiIOField, HmiText, HmiRectangle or HmiCircle is api_member_missing (no such type), a batch is applied
    /// all-or-nothing.
    /// </summary>
    private string Screens(string method, HmiWriteRequestInfo w)
    {
        var outcome = new HmiWriteOutcomeInfo { ObjectName = w.ItemName ?? w.ScreenName ?? "H" };
        string Fail(string category, string message) => JsonSerializer.Serialize(new WorkerResponse { Success = false, FailureCategory = category, Error = message, HmiWriteOutcome = outcome }, WorkerJson.Envelope);
        (List<(string Name, string Type)> Items, string Script) screen = default;
        if (w.ScreenName is not null && method != "hmi_create_screen" && !_screens.TryGetValue(w.ScreenName, out screen)) return Missing();
        switch (method)
        {
            case "hmi_create_screen":
                if (_screens.ContainsKey(w.ScreenName!)) return Rejected("Error when calling method 'Create' of type 'HmiScreenComposition'.\r\n\r\n-The value must be unique.\r\n", outcome);
                _screens[w.ScreenName!] = (new(), string.Empty);
                outcome.AppliedProperties.Add("Create");
                if (w.Width is not null) outcome.AppliedProperties.Add("Width");
                if (w.Height is not null) outcome.AppliedProperties.Add("Height");
                break;
            case "hmi_delete_screen": _screens.Remove(w.ScreenName!); outcome.AppliedProperties.Add("Delete"); break;
            case "hmi_create_screen_item":
                if (w.ItemType is not ("HmiButton" or "HmiIOField" or "HmiText" or "HmiRectangle" or "HmiCircle"))
                    return Fail("api_member_missing", $"This TIA Portal installation has no public HmiScreenItemBase type named exactly '{w.ItemType}'. Nothing was changed.");
                screen.Items.Add((w.ItemName!, w.ItemType));
                outcome.AppliedProperties.Add("Create");
                break;
            case "hmi_delete_screen_item":
                if (screen.Items.RemoveAll(i => Same(i.Name, w.ItemName)) == 0) return Missing();
                outcome.AppliedProperties.Add("Delete");
                break;
            case "hmi_set_screen_item_property":
            case "hmi_set_dynamization_tag":
                if (w.ItemName is not null && !screen.Items.Any(i => Same(i.Name, w.ItemName))) return Missing();
                outcome.AppliedProperties.Add(method == "hmi_set_dynamization_tag" ? "Tag" : w.PropertyName!);
                break;
            case "hmi_replace_in_scripts":
                foreach (var name in _screens.Keys.Where(k => w.ScreenName is null || Same(k, w.ScreenName)).ToList())
                {
                    var (items, script) = _screens[name];
                    var replaced = TiaMcpServer.Contracts.Plc.Interface.SimaticMlNetworkTexts.ReplaceAll(script, w.Replacements!);
                    if (replaced == script) continue;
                    _screens[name] = (items, replaced);
                    outcome.AppliedProperties.Add($"{name} event Loaded ScriptCode");
                }

                if (outcome.AppliedProperties.Count == 0) outcome.AppliedProperties.Add("(no script contained a find)");
                break;
            case "hmi_rename_screens":
            case "hmi_rename_tags":
                var renames = w.Renames ?? new();
                var screensBatch = method == "hmi_rename_screens";
                if (renames.Any(x => screensBatch ? !_screens.ContainsKey(x.Name) : !_tags.Any(t => Same(t.Name, x.Name)))) return Missing();
                if (renames.Any(x => x.NewName.StartsWith("RejectedByPortal", StringComparison.Ordinal)))
                    return Rejected("The name is not valid. TIA Portal rolled the transaction back.", outcome with { AppliedProperties = new() });
                var steps = TiaMcpServer.Contracts.Plc.PlcBatchRenamePlan.Plan(renames.Select(x => x.Name).ToList(), renames.Select(x => x.NewName).ToList(), w.TemporaryStamp ?? "fake");
                var resolved = renames.Select(x => screensBatch ? (object)x.Name : _tags.First(t => Same(t.Name, x.Name))).ToList();
                var current = renames.Select(x => x.Name).ToList();
                foreach (var (index, name) in steps)
                {
                    if (screensBatch)
                    {
                        var entry = _screens[current[index]];
                        _screens.Remove(current[index]);
                        _screens[name] = entry;
                    }
                    else ((HmiTagRowInfo)resolved[index]).Name = name;
                    outcome.AppliedProperties.Add($"Name '{renames[index].Name}' -> '{name}'");
                    current[index] = name;
                }

                outcome.ObjectName = string.Join(", ", renames.Select(x => x.Name));
                break;
            default: return Missing();
        }

        return Ok(outcome);
    }

    /// <summary>
    /// The fixture's user text lists and a dictionary standing for the file system. A directory or file path containing
    /// "NoMember" stands for an installation without the runtime-bound members (api_member_missing, nothing written);
    /// C:\Imports\Lists.xml holds the new list "Valves".
    /// </summary>
    private readonly List<string> _textLists = ["States", "Modes"];
    private readonly Dictionary<string, List<string>> _textListFiles = new(StringComparer.OrdinalIgnoreCase) { [@"C:\Imports\Lists.xml"] = ["Valves"] };

    private string TextListFiles(string method, HmiWriteRequestInfo w)
    {
        var outcome = new HmiWriteOutcomeInfo { ObjectName = "H" };
        string Fail(string category, string message) => JsonSerializer.Serialize(new WorkerResponse { Success = false, FailureCategory = category, Error = message, HmiWriteOutcome = outcome }, WorkerJson.Envelope);
        var path = (method == HmiTextListMethods.ExportTextLists ? w.DirectoryPath : w.FilePath)!.Replace('/', '\\');
        if (path.Contains("NoMember", StringComparison.OrdinalIgnoreCase))
            return Fail("api_member_missing", "Export(FileInfo, ExportOptions) on HmiTextList is not available on this installation; no Siemens document describes it for WinCC Unified text lists. Nothing was changed.");
        if (method == HmiTextListMethods.ExportTextLists)
        {
            var names = w.TextListNames ?? _textLists;
            if (names.FirstOrDefault(n => !_textLists.Contains(n, StringComparer.OrdinalIgnoreCase)) is { } missing)
                return Fail("target_not_found", $"HMI 'H' has no user text list '{missing}'. Nothing was written.");
            foreach (var name in names)
            {
                var file = $"{path.TrimEnd('\\')}\\{name}.xml";
                if (_textListFiles.ContainsKey(file) && w.Overwrite != true) return Fail("validation_error", $"'{file}' already exists; set overwrite:true to replace it. Nothing was written.");
            }

            foreach (var name in names)
            {
                var file = $"{path.TrimEnd('\\')}\\{name}.xml";
                _textListFiles[file] = [name];
                outcome.AppliedProperties.Add($"textList {name} -> {file} bytes=10 sha256={new string('a', 64)}");
            }

            return Ok(outcome);
        }

        if (!_textListFiles.TryGetValue(path, out var lists)) return Fail("target_not_found", $"'{path}' does not exist. Nothing was imported.");
        outcome.AppliedProperties.Add($"import {path} bytes=10 sha256={new string('b', 64)} options={(w.Overwrite == true ? "Override" : "None")}");
        if (lists.Any(l => _textLists.Contains(l, StringComparer.OrdinalIgnoreCase)) && w.Overwrite != true)
            return Fail("engineering_rejected", "TIA Portal rejected the operation: the composition already contains a text list of that name.");
        var created = lists.Where(l => !_textLists.Contains(l, StringComparer.OrdinalIgnoreCase)).ToList();
        _textLists.AddRange(created);
        outcome.AppliedProperties.AddRange(created.Select(name => "textList " + name));
        if (created.Count == 0) outcome.AppliedProperties.Add("(no new text list: with overwrite an existing list was replaced; which one is not reported by TIA Portal)");
        return Ok(outcome);
    }

    // ---- Classic WinCC (issue #28) ----------------------------------------------------------------------

    /// <summary>
    /// The classic HMI "C": objects per kind with their user folder, user folders per kind, and a dictionary standing for
    /// the file system. A path containing "NoMember" stands for an installation without the runtime-bound member
    /// (api_member_missing, nothing changed); C:\Imports\Screens.xml holds the screen "Detail" with number 2,
    /// C:\Imports\Clash.xml the screen "Other" with number 1, which "Main" already uses (V21 pitfall 4).
    /// </summary>
    private readonly Dictionary<string, List<(string Name, string? Folder)>> _classic = new(StringComparer.Ordinal)
    {
        [HmiClassicKinds.Screens] = [("Main", null), ("Settings", "Service")],
        [HmiClassicKinds.TagTables] = [("Default tag table", null), ("Motors", null)],
        [HmiClassicKinds.TextLists] = [("States", null)],
        [HmiClassicKinds.GraphicLists] = [("Lamps", null)],
        [HmiClassicKinds.Connections] = [("HMI_Connection_1", null)],
    };
    private readonly Dictionary<string, HashSet<string>> _classicFolders = new(StringComparer.Ordinal)
    {
        [HmiClassicKinds.Screens] = new(StringComparer.OrdinalIgnoreCase) { "Service" },
    };
    private readonly Dictionary<string, (string Name, int Number)?> _classicFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        [@"C:\Imports\Screens.xml"] = ("Detail", 2),
        [@"C:\Imports\Clash.xml"] = ("Other", 1),
    };
    private static readonly Dictionary<string, int> ClassicScreenNumbers = new(StringComparer.OrdinalIgnoreCase) { ["Main"] = 1, ["Settings"] = 3 };

    private List<(string Name, string? Folder)> ClassicObjects(string kind) => _classic.TryGetValue(kind, out var list) ? list : _classic[kind] = new();

    private string ClassicList(HmiQueryInfo q)
    {
        if (q.HmiName is not null && !Same(q.HmiName, "C") && !Same(q.HmiName, "Panel"))
            return JsonSerializer.Serialize(new WorkerResponse { Success = false, FailureCategory = "target_kind_unsupported", Error = $"HMI '{q.HmiName}' is a WinCC Unified HMI; the classic operations need a Classic (WinCC) HMI." }, WorkerJson.Envelope);
        var objects = ClassicObjects(q.ClassicKind!).Where(o => q.GroupPath is null || Same(o.Folder, q.GroupPath) || (o.Folder ?? string.Empty).StartsWith(q.GroupPath + "/", StringComparison.OrdinalIgnoreCase));
        return Ok(new HmiClassicObjectListInfo
        {
            HmiName = "C", DeviceName = "Panel", Kind = q.ClassicKind!, GroupPath = q.GroupPath, TableName = q.TableName,
            Objects = objects.Select(o => new HmiClassicObjectInfo { Name = o.Name, FolderPath = o.Folder, TypeName = "Synthetic", IsSystemObject = o.Name == "Default tag table" ? true : null }).ToList(),
            Folders = _classicFolders.TryGetValue(q.ClassicKind!, out var folders) ? folders.OrderBy(f => f).ToList() : [],
        });
    }

    private string Classic(string method, HmiWriteRequestInfo w)
    {
        var outcome = new HmiWriteOutcomeInfo { ObjectName = w.ObjectName ?? w.GroupPath ?? "C" };
        string Fail(string category, string message) => JsonSerializer.Serialize(new WorkerResponse { Success = false, FailureCategory = category, Error = message, HmiWriteOutcome = outcome }, WorkerJson.Envelope);
        if (w.HmiName != "C") return Fail("target_kind_unsupported", $"HMI '{w.HmiName}' is a WinCC Unified HMI; the classic operations need a Classic (WinCC) HMI.");
        var kind = w.ClassicKind!;
        var objects = ClassicObjects(kind);
        var path = (w.DirectoryPath ?? w.FilePath ?? string.Empty).Replace('/', '\\');
        if (path.Contains("NoMember", StringComparison.OrdinalIgnoreCase))
            return Fail("api_member_missing", "Screen.Export(FileInfo, ExportOptions): This TIA Portal installation exposes no Export. Nothing was changed.");
        switch (method)
        {
            case HmiClassicMethods.ExportObjects:
                var selected = (w.ObjectNames ?? objects.Where(o => w.GroupPath is null || Same(o.Folder, w.GroupPath)).Select(o => o.Name).ToList()).ToList();
                if (selected.FirstOrDefault(n => !objects.Any(o => Same(o.Name, n))) is { } absent) return Fail("target_not_found", $"No classic {kind} object '{absent}' was found in the selected scope.");
                foreach (var name in selected)
                {
                    var file = $"{path.TrimEnd('\\')}\\{name}.xml";
                    if (_classicFiles.ContainsKey(file) && w.Overwrite != true) return Fail("validation_error", $"'{file}' already exists; set overwrite:true to replace it. Nothing was written.");
                }

                foreach (var name in selected)
                {
                    var file = $"{path.TrimEnd('\\')}\\{name}.xml";
                    _classicFiles[file] = kind == HmiClassicKinds.Screens ? (name, ClassicScreenNumbers.GetValueOrDefault(name, 9)) : (name, 0);
                    outcome.AppliedProperties.Add($"{kind} {name} -> {file} bytes=10 sha256={new string('c', 64)}");
                }

                return Ok(outcome);
            case HmiClassicMethods.ImportObjects:
                if (!_classicFiles.TryGetValue(path, out var content) || content is null) return Fail("target_not_found", $"'{path}' does not exist. Nothing was imported.");
                outcome.AppliedProperties.Add($"import {path} bytes=10 sha256={new string('d', 64)} options={(w.Overwrite == true ? "Override" : "None")}");
                if (kind == HmiClassicKinds.Screens && ClassicScreenNumbers.FirstOrDefault(n => n.Value == content.Value.Number && !Same(n.Key, content.Value.Name)) is { Key: not null } clash)
                    return Fail("validation_error", $"The import is refused: screen '{content.Value.Name}' has number {content.Value.Number}, which existing screen '{clash.Key}' uses. A classic screen import with a duplicate screen number is reported to terminate TIA Portal (V21 pitfall 4). Change the <Number> in the file. Nothing was imported.");
                if (objects.Any(o => Same(o.Name, content.Value.Name)))
                {
                    if (w.Overwrite != true) return Rejected($"The {kind} object '{content.Value.Name}' already exists.", outcome);
                    outcome.AppliedProperties.Add($"(no new {kind} object: with overwrite an existing one was replaced; which one is not reported by TIA Portal)");
                    return Ok(outcome);
                }

                objects.Add((content.Value.Name, w.GroupPath));
                outcome.AppliedProperties.Add($"{kind} {content.Value.Name}");
                return Ok(outcome);
            case HmiClassicMethods.DeleteObject:
                if (objects.RemoveAll(o => Same(o.Name, w.ObjectName)) == 0) return Fail("target_not_found", $"No classic {kind} object '{w.ObjectName}' was found in the selected scope.");
                outcome.AppliedProperties.Add("Delete");
                outcome.AppliedProperties.Add("readBack gone");
                return Ok(outcome);
            case HmiClassicMethods.CreateFolder:
                if (!_classicFolders.TryGetValue(kind, out var folders)) _classicFolders[kind] = folders = new(StringComparer.OrdinalIgnoreCase);
                if (!folders.Add(w.GroupPath!)) return Rejected($"The folder '{w.GroupPath}' already exists.", outcome);
                outcome.AppliedProperties.Add("Create");
                outcome.AppliedProperties.Add("readBack present");
                return Ok(outcome);
            case HmiClassicMethods.DeleteFolder:
                if (!_classicFolders.TryGetValue(kind, out var existing) || !existing.Remove(w.GroupPath!)) return Fail("target_not_found", $"The folder '{w.GroupPath}' was not found.");
                objects.RemoveAll(o => Same(o.Folder, w.GroupPath));
                outcome.AppliedProperties.Add("Delete");
                outcome.AppliedProperties.Add("readBack gone");
                return Ok(outcome);
        }

        return Missing();
    }

    // ---- WinCC Unified events and dynamizations (issue #29) -------------------------------------------

    /// <summary>
    /// Event handlers and dynamizations per "screen/item" of the Unified HMI "H" (screen Main, item Button_1). The button's
    /// event enum has None, Tapped, Up and Down; another eventType is a validation_error naming them, as the worker reports it.
    /// </summary>
    private readonly Dictionary<string, Dictionary<string, string>> _events = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, (string Type, string? Code)>> _dynamizations = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string[] ButtonEvents = ["None", "Tapped", "Up", "Down"];

    private string? EventTarget(string? screen, string? item)
    {
        if (screen is null || !_screens.TryGetValue(screen, out var found)) return null;
        if (item is not null && !found.Items.Any(i => Same(i.Name, item))) return null;
        return $"{screen}/{item ?? "(screen)"}";
    }

    private string EventsRead(string method, HmiQueryInfo q)
    {
        if (EventTarget(q.ScreenName, q.ItemName) is not { } key) return Missing();
        if (method == HmiScreenEventMethods.ListEvents)
        {
            var events = _events.GetValueOrDefault(key) ?? new();
            return Ok(new HmiScreenItemEventsInfo
            {
                ScreenName = q.ScreenName!, ItemName = q.ItemName, TypeName = q.ItemName is null ? "HmiScreen" : "HmiButton",
                Events = events.Where(e => !e.Key.Contains('|')).Select(e => new HmiEventScriptInfo { EventType = e.Key, ScriptCode = e.Value, Async = false }).ToList(),
                PropertyEvents = events.Where(e => e.Key.Contains('|')).Select(e => new HmiEventScriptInfo { PropertyName = e.Key.Split('|')[0], EventType = e.Key.Split('|')[1], ScriptCode = e.Value }).ToList(),
            });
        }

        return Ok(new HmiScreenItemDynamizationsInfo
        {
            ScreenName = q.ScreenName!, ItemName = q.ItemName, TypeName = q.ItemName is null ? "HmiScreen" : "HmiButton",
            Dynamizations = (_dynamizations.GetValueOrDefault(key) ?? new()).Select(d => new HmiDynamizationInfo
            {
                PropertyName = d.Key, TypeName = d.Value.Type, DynamizationType = d.Value.Type.Replace("Dynamization", string.Empty),
                Values = d.Value.Code is null ? [] : [new HmiNamedValueInfo { Name = "ScriptCode", Value = d.Value.Code }],
            }).ToList(),
        });
    }

    private string Events(string method, HmiWriteRequestInfo w)
    {
        var outcome = new HmiWriteOutcomeInfo { ObjectName = $"{w.ScreenName}/{w.ItemName ?? "(screen)"}" };
        string Fail(string category, string message) => JsonSerializer.Serialize(new WorkerResponse { Success = false, FailureCategory = category, Error = message, HmiWriteOutcome = outcome }, WorkerJson.Envelope);
        if (EventTarget(w.ScreenName, w.ItemName) is not { } key) return Missing();
        var events = _events.TryGetValue(key, out var e) ? e : _events[key] = new(StringComparer.OrdinalIgnoreCase);
        var dynamizations = _dynamizations.TryGetValue(key, out var d) ? d : _dynamizations[key] = new(StringComparer.OrdinalIgnoreCase);
        switch (method)
        {
            case HmiScreenEventMethods.SetEventScript:
            case HmiScreenEventMethods.DeleteEventScript:
                var members = w.PropertyName is null ? ButtonEvents : ["Change", "QualityCodeChange"];
                if (!members.Contains(w.EventType, StringComparer.Ordinal))
                    return Fail("validation_error", $"'{w.EventType}' is not a member of {(w.PropertyName is null ? "HmiButtonEventType" : "PropertyEventType")} ({string.Join(", ", members)}); nothing was changed.");
                var eventKey = w.PropertyName is null ? w.EventType! : $"{w.PropertyName}|{w.EventType}";
                if (method == HmiScreenEventMethods.DeleteEventScript)
                {
                    if (!events.Remove(eventKey)) return Fail("target_not_found", $"'{key}' has no event handler to delete.");
                    outcome.AppliedProperties.AddRange(["Delete", "readBack gone"]);
                    return Ok(outcome);
                }

                if (!events.ContainsKey(eventKey)) outcome.AppliedProperties.Add("Create");
                events[eventKey] = w.ScriptCode!;
                outcome.AppliedProperties.Add("ScriptCode");
                if (w.GlobalDefinitionAreaScriptCode is not null) outcome.AppliedProperties.Add("GlobalDefinitionAreaScriptCode");
                if (w.Async is not null) outcome.AppliedProperties.Add("Async");
                return Ok(outcome);
            case HmiScreenEventMethods.SetDynamizationScript:
            case HmiScreenEventMethods.SetDynamizationFlashing:
                var type = method == HmiScreenEventMethods.SetDynamizationScript ? "ScriptDynamization" : "FlashingDynamization";
                if (dynamizations.TryGetValue(w.PropertyName!, out var existing) && existing.Type != type)
                    return Fail("target_kind_unsupported", $"'{w.PropertyName}' is dynamized by a {existing.Type}, not a {type}; delete it with delete_dynamization first. Nothing was changed.");
                if (existing.Type is null) outcome.AppliedProperties.Add("Create");
                dynamizations[w.PropertyName!] = (type, w.ScriptCode);
                if (w.ScriptCode is not null) outcome.AppliedProperties.Add("ScriptCode");
                if (w.TriggerType is not null) outcome.AppliedProperties.Add("Trigger.Type");
                if (w.TriggerTags is not null) outcome.AppliedProperties.Add("Trigger.Tags");
                foreach (var (name, value) in new[] { ("Color", w.Color), ("AlternateColor", w.AlternateColor), ("FlashingCondition", w.FlashingCondition), ("FlashingRate", w.FlashingRate) })
                    if (value is not null) outcome.AppliedProperties.Add(name);
                return Ok(outcome);
            case HmiScreenEventMethods.DeleteDynamization:
                if (!dynamizations.Remove(w.PropertyName!)) return Fail("target_not_found", $"'{w.PropertyName}' of '{key}' has no dynamization to delete.");
                outcome.AppliedProperties.AddRange(["Delete", "readBack gone"]);
                return Ok(outcome);
        }

        return Missing();
    }

    // ---- WinCC Unified audit classes, runtime settings and plant views (issue #29) ----------------------

    /// <summary>
    /// The audit class "Audit_1", runtime settings by "section.property", and the plant view "Plant" with the node
    /// "Line1" (plant object type "Motor" with the interface "Speed"). A plantViewName "NoProvider" stands for a project
    /// whose services list no PlantViewsProvider (api_member_missing).
    /// </summary>
    private readonly List<string> _auditClasses = ["Audit_1"];
    private readonly Dictionary<string, string> _runtimeSettings = new(StringComparer.OrdinalIgnoreCase) { ["StartScreen"] = "Main" };
    private readonly Dictionary<string, List<(string Path, string? Type)>> _plantViews = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Plant"] = [("Line1", "Motor")],
    };

    private string PlantViews(HmiQueryInfo q)
    {
        var views = _plantViews.Where(v => q.PlantViewName is null || Same(v.Key, q.PlantViewName)).ToList();
        if (q.PlantViewName is not null && views.Count == 0) return Missing();
        return Ok(new HmiPlantViewsInfo
        {
            ProviderOwner = "project",
            PlantViews = views.Select(v => new HmiPlantViewInfo
            {
                Name = v.Key,
                Nodes = v.Value.Select(n => new HmiPlantViewNodeInfo { Name = n.Path.Split('/')[^1], NodePath = n.Path, ViewPath = v.Key + "/" + n.Path, PlantObjectType = n.Type, Interfaces = n.Type is null ? [] : ["Speed"] }).ToList(),
            }).ToList(),
        });
    }

    private string Configuration(string method, HmiWriteRequestInfo w)
    {
        var outcome = new HmiWriteOutcomeInfo { ObjectName = w.AuditClassName ?? w.PlantViewName ?? w.SettingsSection ?? "runtime settings" };
        string Fail(string category, string message) => JsonSerializer.Serialize(new WorkerResponse { Success = false, FailureCategory = category, Error = message, HmiWriteOutcome = outcome }, WorkerJson.Envelope);
        if (w.PlantViewName == "NoProvider")
            return Fail("api_member_missing", $"No {HmiUnifiedConfigurationMethods.PlantViewsProviderType} was found among the services of the HMI software or the project (hmiSoftware: not listed; project: not listed). Nothing was changed.");
        switch (method)
        {
            case HmiUnifiedConfigurationMethods.CreateAuditClass:
                if (_auditClasses.Contains(w.AuditClassName!, StringComparer.OrdinalIgnoreCase)) return Rejected("The value must be unique.", outcome);
                _auditClasses.Add(w.AuditClassName!);
                outcome.AppliedProperties.AddRange(["Create", "Name", "readBack present"]);
                return Ok(outcome);
            case HmiUnifiedConfigurationMethods.DeleteAuditClass:
                if (_auditClasses.RemoveAll(a => Same(a, w.AuditClassName)) == 0) return Fail("target_not_found", $"No alarm audit class '{w.AuditClassName}' was found.");
                outcome.AppliedProperties.AddRange(["Delete", "readBack gone"]);
                return Ok(outcome);
            case HmiUnifiedConfigurationMethods.SetAuditClassProperty:
                if (!_auditClasses.Contains(w.AuditClassName!, StringComparer.OrdinalIgnoreCase)) return Fail("target_not_found", $"No alarm audit class '{w.AuditClassName}' was found.");
                outcome.AppliedProperties.Add(w.PropertyName!);
                return Ok(outcome);
            case HmiUnifiedConfigurationMethods.SetRuntimeSetting:
                _runtimeSettings[(w.SettingsSection is null ? string.Empty : w.SettingsSection + ".") + w.PropertyName] = w.Value!;
                outcome.AppliedProperties.Add(w.PropertyName!);
                return Ok(outcome);
            // Package G stand-ins (no Siemens evidence): screens resize, logs "DataLog_1"/"AlarmLog_1", OPC UA alarm type "OpcUaAlarm_1".
            case HmiUnifiedConfigurationMethods.ResizeScreen:
                outcome.AppliedProperties.AddRange(["ResizeScreen", "size 800x600 -> 1920x1080"]);
                return Ok(outcome);
            case HmiUnifiedConfigurationMethods.SetLogDuration:
                if (!Same(w.ObjectName, w.PropertyName!.StartsWith("AlarmLog", StringComparison.Ordinal) ? "AlarmLog_1" : "DataLog_1"))
                    return Fail("target_not_found", $"No log '{w.ObjectName}' was found. Nothing was changed.");
                outcome.AppliedProperties.AddRange([w.PropertyName.EndsWith("SegmentTimePeriod", StringComparison.Ordinal) ? "SetSegmentDuration" : "SetLogDuration", $"readBack {w.Value}"]);
                return Ok(outcome);
            case HmiUnifiedConfigurationMethods.SetOpcUaAlarmTypeNode:
                if (!Same(w.ObjectName, "OpcUaAlarm_1")) return Fail("target_not_found", $"No OPC UA alarm type '{w.ObjectName}' was found. Nothing was changed.");
                outcome.AppliedProperties.AddRange(["SetNodeIdAndConnection", $"readBack NodeId {w.Value}, Connection {w.Connection}"]);
                return Ok(outcome);
            case HmiUnifiedConfigurationMethods.CreatePlantView:
                if (_plantViews.ContainsKey(w.PlantViewName!)) return Rejected("The value must be unique.", outcome);
                _plantViews[w.PlantViewName!] = [];
                outcome.AppliedProperties.AddRange(["Create", "readBack present"]);
                return Ok(outcome);
            case HmiUnifiedConfigurationMethods.DeletePlantView:
                if (!_plantViews.Remove(w.PlantViewName!)) return Fail("target_not_found", $"No plant view '{w.PlantViewName}' was found.");
                outcome.AppliedProperties.AddRange(["Delete", "readBack gone"]);
                return Ok(outcome);
        }

        if (!_plantViews.TryGetValue(w.PlantViewName!, out var nodes)) return Fail("target_not_found", $"No plant view '{w.PlantViewName}' was found.");
        switch (method)
        {
            case HmiUnifiedConfigurationMethods.CreatePlantViewNode:
                if (w.NodePath is not null && !nodes.Any(n => Same(n.Path, w.NodePath))) return Fail("target_not_found", $"The plant view node '{w.NodePath}' was not found.");
                nodes.Add((w.NodePath is null ? w.NodeName! : w.NodePath + "/" + w.NodeName, w.PlantObjectType));
                outcome.AppliedProperties.AddRange(["Create", "readBack present"]);
                return Ok(outcome);
            case HmiUnifiedConfigurationMethods.DeletePlantViewNode:
                if (nodes.RemoveAll(n => Same(n.Path, w.NodePath) || n.Path.StartsWith(w.NodePath + "/", StringComparison.OrdinalIgnoreCase)) == 0)
                    return Fail("target_not_found", $"The plant view node '{w.NodePath}' was not found.");
                outcome.AppliedProperties.AddRange(["Delete", "readBack gone"]);
                return Ok(outcome);
            case HmiUnifiedConfigurationMethods.SetPlantObjectInterfaceProperty:
                if (nodes.FirstOrDefault(n => Same(n.Path, w.NodePath)) is not { Type: not null }) return Fail("target_not_found", $"The plant view node '{w.NodePath}' has no plant object.");
                if (!Same(w.InterfaceName, "Speed")) return Fail("target_not_found", $"0 plant object interfaces of '{w.NodePath}' are named '{w.InterfaceName}'.");
                outcome.AppliedProperties.Add(w.PropertyName!);
                return Ok(outcome);
        }

        return Missing();
    }
}
