using TiaMcpServer.Contracts.Plc.Safety;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using M = TiaMcpServer.Contracts.Plc.Settings.PlcSafetyValidationMethods;
using N = TiaMcpServer.Contracts.Plc.Settings.PlcSafetyValidationNames;

namespace TiaMcpServer.Contracts.Plc.Settings;

/// <summary>
/// Issue #25: the Safety Validation Assistant (see <see cref="PlcSafetyValidationMethods"/> for the evidence). The MCP reads,
/// prepares (tests, safety functions, conditions) and generates reports; it never records a test result: <c>TestState</c> and
/// <c>OverallState</c> are never written, and imports or copies that could carry executed results are not built.
/// <c>SafetyValidationAssistant</c> is taken from the Project's services (where it hangs is not documented).
/// </summary>
public static partial class PlcSettingsRun
{
    private static readonly string[] TestProperties = { "Name", "Author", "EvaluationDeviceName", "OverallState", "Group" };
    private static readonly string[] FunctionReadProperties = { "Name", "TestName", "Description", "TestState", "IsTraced" };
    private static readonly string[] ConditionReadProperties = { "DeviceName", "SignalName", "SignalUsage", "InitialInput", "ExecutedInput", "Response", "Comment" };

    private static (object Assistant, object Owner) Assistant(PlcSettingsContext context)
        => RequireService(context, new[] { RequireProject(context) }, N.Assistant, "The project");

    /// <summary>A service a Safety Validation Assistant object offers (GetServiceInfos/GetService), or null.</summary>
    private static object? ChildService(PlcSettingsContext context, object owner, string fullName)
        => RuntimeServiceValues.Find(context.ServiceTypes(owner), type => context.Service(owner, type), fullName).Service;

    /// <summary>Every activation test with its group path ("/" for the root), depth first.</summary>
    private static List<(object Test, string Group)> AllTests(object assistant)
    {
        var list = new List<(object, string)>();
        foreach (var test in Enumerate(Get(assistant, "ActivationTests"))) if (test is not null) list.Add((test, "/"));
        void Walk(object? groups, string path)
        {
            foreach (var group in Enumerate(groups))
            {
                if (group is null) continue;
                var groupPath = (path == "/" ? "/" : path + "/") + PlcSettingsContext.DefaultName(group);
                foreach (var test in Enumerate(Get(group, "ActivationTests"))) if (test is not null) list.Add((test, groupPath));
                Walk(Get(group, "Groups"), groupPath);
            }
        }

        Walk(Get(assistant, "ActivationTestGroups"), "/");
        return list;
    }

    private static object FindTest(PlcSettingsContext context, object assistant, string? name)
    {
        var testName = name ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, "The activation test name is required. Nothing was changed.");
        var matches = AllTests(assistant).Where(t => context.NameOf(t.Test) == testName).ToList();
        return matches.Count switch
        {
            1 => matches[0].Test,
            0 => throw new RuntimeBindingException(WorkerFailureCategories.TargetNotFound, $"The project has no activation test '{testName}'. Nothing was changed."),
            _ => throw new RuntimeBindingException(WorkerFailureCategories.TargetAmbiguous, $"Several activation tests are named '{testName}'. Nothing was changed."),
        };
    }

    /// <summary>The group at <paramref name="path"/> (null for the root "/"); a missing segment is target_not_found.</summary>
    private static object? FindGroup(PlcSettingsContext context, object assistant, string? path)
    {
        object? current = null;
        foreach (var segment in (path ?? "/").Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var groups = current is null ? Get(assistant, "ActivationTestGroups") : Get(current, "Groups");
            current = Enumerate(groups).FirstOrDefault(g => g is not null && context.NameOf(g) == segment)
                ?? throw new RuntimeBindingException(WorkerFailureCategories.TargetNotFound, $"The activation test group '{path}' was not found (segment '{segment}'). Nothing was changed.");
        }

        return current;
    }

    private static object FindFunction(PlcSettingsContext context, object test, string? name)
    {
        var functionName = name ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, "functionName is required. Nothing was changed.");
        var matches = Enumerate(Get(test, "SafetyFunctions")).Where(f => f is not null && context.NameOf(f) == functionName).ToList();
        return matches.Count switch
        {
            1 => matches[0]!,
            0 => throw new RuntimeBindingException(WorkerFailureCategories.TargetNotFound, $"Activation test '{context.NameOf(test)}' has no safety function '{functionName}'. Nothing was changed."),
            _ => throw new RuntimeBindingException(WorkerFailureCategories.TargetAmbiguous, $"Activation test '{context.NameOf(test)}' has several safety functions '{functionName}'. Nothing was changed."),
        };
    }

    private static object FindCondition(object function, int? position)
    {
        var index = position ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, "position (1-based) is required. Nothing was changed.");
        var conditions = Elements(Get(function, "Conditions"), "SafetyFunction.Conditions");
        return index >= 1 && index <= conditions.Count && conditions[index - 1] is { } condition ? condition
            : throw new RuntimeBindingException(WorkerFailureCategories.TargetNotFound, $"The safety function has {Text(conditions.Count)} condition(s); position {Text(index)} is not one. Nothing was changed.");
    }

    /// <summary>A device item of a list a method returns (DeviceQuery.EvaluationDevices(), ActivationTest.AvailableDevices()) by name.</summary>
    private static object DeviceFrom(PlcSettingsContext context, object owner, string method, string? name, string field)
    {
        var deviceName = name ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, $"{field} is required. Nothing was changed.");
        var call = RequireMethod(owner, method, Type.EmptyTypes, $"{method}()");
        var devices = Enumerate(Call(call, owner)).Where(d => d is not null).ToList();
        var matches = devices.Where(d => context.NameOf(d) == deviceName).ToList();
        return matches.Count == 1 ? matches[0]!
            : throw new RuntimeBindingException(matches.Count == 0 ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous,
                $"{method}() lists {(matches.Count == 0 ? "no" : "several")} device item(s) named '{deviceName}' (offered: {string.Join(", ", devices.Select(d => context.NameOf(d)))}). Nothing was changed.");
    }

    private static PlcSettingsReadInfo ReadSafetyValidation(string method, PlcSettingsContext? project, PlcSettingsReadInfo result, PlcSettingsRequestInfo info)
    {
        if (project is null) return result;
        var owners = project.Project is null ? Array.Empty<object>() : new[] { project.Project };
        if (method == M.CheckActivationTest)
        {
            var (assistant, owner) = Assistant(project);
            var test = FindTest(project, assistant, info.TestName);
            object target = test;
            var what = $"activation test '{info.TestName}'";
            if (info.FunctionName is not null)
            {
                target = FindFunction(project, test, info.FunctionName);
                what = $"safety function '{info.FunctionName}' of {what}";
                if (info.Position is not null)
                {
                    target = FindCondition(target, info.Position);
                    what = $"condition {Text(info.Position.Value)} of {what}";
                }
            }

            var validity = ChildService(project, target, N.TestValidity)
                ?? throw new RuntimeBindingException(WorkerFailureCategories.UnsupportedCapability, $"The {what} offers no {N.TestValidity}.");
            var check = RequireMethod(validity, "CheckValidity", Type.EmptyTypes, "TestValidity.CheckValidity()");
            var checkResult = Call(check, validity) ?? throw Missing("TestValidity.CheckValidity()");
            result.Project.Add(new PlcSettingsSectionInfo
            {
                Name = "testValidity", Service = N.TestValidity, Offered = true, Owner = project.Owner(owner),
                Properties =
                {
                    new PlcSettingPropertyInfo { Name = "Target", Value = RuntimeServiceValues.Project(what) },
                    new PlcSettingPropertyInfo { Name = "State", Value = ReadValue(project, checkResult, "State") },
                    new PlcSettingPropertyInfo { Name = "ErrorCount", Value = ReadValue(project, checkResult, "ErrorCount") },
                    new PlcSettingPropertyInfo { Name = "Messages", Value = Names(project, checkResult, "Messages") },
                },
                Note = "TestValidity checks whether the test data is consistent with the project; it is no test result and no acceptance. " + PlcSafetyPolicy.AcceptanceNotice,
            });
            return result;
        }

        var groupPaths = new Dictionary<object, string>(ReferenceComparer.Instance);
        result.Project.Add(Section(project, "safetyValidation", owners, N.Assistant, Array.Empty<string>(), assistant =>
        {
            var tests = AllTests(assistant);
            foreach (var (test, group) in tests) groupPaths[test] = group;
            var collections = new List<PlcSettingsCollectionInfo>
            {
                Collection(project, "ActivationTests", () => tests.Select(t => t.Test).ToList(), "Name", TestProperties,
                    nested: test => new List<PlcSettingsCollectionInfo>
                    {
                        Collection(project, "SafetyFunctions", () => Get(test, "SafetyFunctions"), "Name", FunctionReadProperties,
                            nested: function => new List<PlcSettingsCollectionInfo>
                            {
                                Collection(project, "Conditions", () => Get(function, "Conditions"), "SignalName", ConditionReadProperties),
                            },
                            special: (function, property) => property == "IsTraced"
                                ? (SafeGet(function, "TraceConfiguration") is { } trace ? ReadValue(project, trace, "IsTraced") : new RuntimeValueInfo { State = RuntimeValueStates.Missing })
                                : null),
                    },
                    special: (test, property) => property == "Group" ? RuntimeServiceValues.Project(groupPaths.TryGetValue(test, out var path) ? path : null) : null),
            };
            var groups = new PlcSettingsCollectionInfo { Name = "ActivationTestGroups" };
            foreach (var path in tests.Select(t => t.Group).Concat(GroupPaths(assistant)).Distinct().Where(p => p != "/").OrderBy(p => p, StringComparer.Ordinal))
                groups.Items.Add(new PlcSettingsItemInfo { Key = path });
            collections.Add(groups);
            var devices = new PlcSettingsCollectionInfo { Name = "EvaluationDevices" };
            try
            {
                if (ChildService(project, assistant, N.DeviceQuery) is { } query)
                    foreach (var device in Enumerate(Call(RequireMethod(query, "EvaluationDevices", Type.EmptyTypes, "DeviceQuery.EvaluationDevices()"), query)))
                        devices.Items.Add(new PlcSettingsItemInfo { Key = project.NameOf(device) });
                else
                    devices.Error = $"The Safety Validation Assistant offers no {N.DeviceQuery}.";
            }
            catch (RuntimeBindingException exception)
            {
                devices.Error = exception.Message;
            }

            collections.Add(devices);
            return collections;
        }, note: "Activation tests are prepared here; their execution and results are recorded by the qualified person in TIA Portal. " + PlcSafetyPolicy.AcceptanceNotice));
        return result;
    }

    private static IEnumerable<string> GroupPaths(object assistant)
    {
        var paths = new List<string>();
        void Walk(object? groups, string path)
        {
            foreach (var group in Enumerate(groups))
            {
                if (group is null) continue;
                var groupPath = (path == "/" ? "/" : path + "/") + PlcSettingsContext.DefaultName(group);
                paths.Add(groupPath);
                Walk(Get(group, "Groups"), groupPath);
            }
        }

        Walk(Get(assistant, "ActivationTestGroups"), "/");
        return paths;
    }

    private static bool WriteSafetyValidation(string method, PlcSettingsDefinition definition, PlcSettingsContext context, PlcSettingsRequestInfo info,
        PlcSettingsResultInfo result, List<string> warnings)
    {
        if (PlcSettingsCatalog.Find(method) is not { Issue: 25 }) return false;
        foreach (var setting in info.Settings)
            if (N.ResultProperties.Contains(setting.Name))
                throw new RuntimeBindingException(WorkerFailureCategories.ValidationError,
                    $"'{setting.Name}' records a test result; this server never sets one (the safety acceptance is the qualified person's). Nothing was changed.");
        var (assistant, owner) = Assistant(context);
        Describe(context, result, owner, N.Assistant);
        warnings.Add(PlcSafetyPolicy.AcceptanceNotice);
        bool TestExists(string name) => AllTests(assistant).Any(t => context.NameOf(t.Test) == name);
        switch (method)
        {
            case M.CreateActivationTest:
            {
                var name = info.Name ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, "name is required. Nothing was changed.");
                result.Target = $"activation test '{name}'";
                var query = ChildService(context, assistant, N.DeviceQuery)
                    ?? throw new RuntimeBindingException(WorkerFailureCategories.UnsupportedCapability, $"The Safety Validation Assistant offers no {N.DeviceQuery}. Nothing was changed.");
                var device = DeviceFrom(context, query, "EvaluationDevices", info.EvaluationDevice, "evaluationDevice");
                var group = FindGroup(context, assistant, info.FolderPath);
                var tests = Get(group ?? assistant, "ActivationTests") ?? throw Missing("ActivationTests");
                var create = RequireMethodByShape(tests, "Create", p => p.Length == 2 && p[0].ParameterType == typeof(string) && p[1].ParameterType.IsInstanceOfType(device),
                    "ActivationTestComposition.Create(String, DeviceItem)");
                Call(create, tests, name, device);
                result.Calls.Add($"ActivationTestComposition.Create(\"{name}\", DeviceItem '{info.EvaluationDevice}')");
                Exists(result, "activationTest", true, TestExists(name), $"Activation test '{name}'", null);
                return true;
            }

            case M.DeleteActivationTest:
            {
                result.Target = $"activation test '{info.Name}'";
                Delete(FindTest(context, assistant, info.Name), "ActivationTest.Delete()", result);
                Exists(result, "activationTest", false, TestExists(info.Name!), $"Activation test '{info.Name}'", null);
                return true;
            }

            case M.ChangeActivationTestEvaluationDevice:
            {
                result.Target = $"activation test '{info.Name}'";
                var test = FindTest(context, assistant, info.Name);
                var query = ChildService(context, assistant, N.DeviceQuery)
                    ?? throw new RuntimeBindingException(WorkerFailureCategories.UnsupportedCapability, $"The Safety Validation Assistant offers no {N.DeviceQuery}. Nothing was changed.");
                var device = DeviceFrom(context, query, "EvaluationDevices", info.EvaluationDevice, "evaluationDevice");
                var before = ReadValue(context, test, "EvaluationDeviceName");
                var change = RequireMethodByShape(test, "ChangeEvaluationDevice", p => p.Length == 1 && p[0].ParameterType.IsInstanceOfType(device), "ActivationTest.ChangeEvaluationDevice(DeviceItem)");
                Call(change, test, device);
                result.Calls.Add($"ActivationTest.ChangeEvaluationDevice(DeviceItem '{info.EvaluationDevice}')");
                // EvaluationDeviceName is not documented to equal the device item's name: reported, not compared.
                result.Changes.Add(new PlcSettingChangeInfo { Name = "EvaluationDeviceName", Requested = info.EvaluationDevice, Before = before, After = ReadValue(context, test, "EvaluationDeviceName") });
                return true;
            }

            case M.CreateActivationTestGroup:
            case M.DeleteActivationTestGroup:
            {
                var path = info.FolderPath ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, "folderPath is required. Nothing was changed.");
                result.Target = $"activation test group '{path}'";
                bool GroupExists() => GroupPaths(assistant).Contains(path.TrimEnd('/'));
                if (method == M.DeleteActivationTestGroup)
                {
                    Delete(FindGroup(context, assistant, path) ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, "The root group cannot be deleted. Nothing was changed."),
                        "ActivationTestUserGroup.Delete()", result);
                    Exists(result, "group", false, GroupExists(), $"Activation test group '{path}'", null);
                    return true;
                }

                var trimmed = path.TrimEnd('/');
                var slash = trimmed.LastIndexOf('/');
                var name = trimmed.Substring(slash + 1);
                if (name.Length == 0) throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, "folderPath must name a group, e.g. /Line1. Nothing was changed.");
                var parent = FindGroup(context, assistant, slash <= 0 ? "/" : trimmed.Substring(0, slash));
                var groups = (parent is null ? Get(assistant, "ActivationTestGroups") : Get(parent, "Groups")) ?? throw Missing("ActivationTestUserGroupComposition");
                var create = RequireMethod(groups, "Create", new[] { typeof(string) }, "ActivationTestUserGroupComposition.Create(String)");
                Call(create, groups, name);
                result.Calls.Add($"ActivationTestUserGroupComposition.Create(\"{name}\")");
                Exists(result, "group", true, GroupExists(), $"Activation test group '{path}'", null);
                return true;
            }
        }

        var testObject = FindTest(context, assistant, info.TestName);
        result.Target = $"activation test '{info.TestName}'";
        switch (method)
        {
            case M.CreateSafetyFunction:
            {
                var functions = Get(testObject, "SafetyFunctions") ?? throw Missing("ActivationTest.SafetyFunctions");
                var create = RequireMethod(functions, "Create", Type.EmptyTypes, "SafetyFunctionComposition.Create()");
                var function = Call(create, functions) ?? throw new RuntimeBindingException(WorkerFailureCategories.PostconditionFailed, "SafetyFunctionComposition.Create() returned null.");
                result.Calls.Add("SafetyFunctionComposition.Create()");
                result.Items.Add($"created safety function '{context.NameOf(function)}'");
                UpdateFunction(context, function, info, definition, result);
                return true;
            }

            case M.UpdateSafetyFunction:
                result.Target += $", safety function '{info.FunctionName}'";
                UpdateFunction(context, FindFunction(context, testObject, info.FunctionName), info, definition, result);
                return true;
            case M.DeleteSafetyFunction:
                result.Target += $", safety function '{info.FunctionName}'";
                Delete(FindFunction(context, testObject, info.FunctionName), "SafetyFunction.Delete()", result);
                Exists(result, "safetyFunction", false, Enumerate(Get(testObject, "SafetyFunctions")).Any(f => f is not null && context.NameOf(f) == info.FunctionName),
                    $"Safety function '{info.FunctionName}'", null);
                return true;
            case M.ResetSafetyFunctionTestResult:
            {
                result.Target += $", safety function '{info.FunctionName}'";
                var function = FindFunction(context, testObject, info.FunctionName);
                var before = ReadValue(context, function, "TestState");
                var reset = RequireMethod(function, "ResetTestResult", Type.EmptyTypes, "SafetyFunction.ResetTestResult()");
                Call(reset, function);
                result.Calls.Add("SafetyFunction.ResetTestResult()");
                Compare(context, result, "TestState", "NotTested", before, ReadValue(context, function, "TestState"), "SafetyFunction");
                warnings.Add("The test result was reset to NotTested; the test has to be executed again by the qualified person in TIA Portal.");
                return true;
            }

            case M.CreateSafetyFunctionCondition:
            {
                result.Target += $", safety function '{info.FunctionName}'";
                var function = FindFunction(context, testObject, info.FunctionName);
                var device = DeviceFrom(context, testObject, "AvailableDevices", info.SignalDevice, "signalDevice");
                var conditions = Get(function, "Conditions") ?? throw Missing("SafetyFunction.Conditions");
                var create = RequireMethodByShape(conditions, "Create", p => p.Length == 3 && p[0].ParameterType.IsInstanceOfType(device) && p[1].ParameterType.IsEnum && p[2].ParameterType == typeof(string),
                    "ConditionComposition.Create(DeviceItem, SignalUsage, String)");
                var usage = ConvertValue(create.GetParameters()[1].ParameterType, info.SignalUsage ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, "signalUsage is required. Nothing was changed."), "signalUsage");
                var count = Elements(conditions, "Conditions").Count;
                var condition = Call(create, conditions, device, usage, info.SignalName ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, "signalName is required. Nothing was changed."));
                result.Calls.Add($"ConditionComposition.Create(DeviceItem '{info.SignalDevice}', SignalUsage.{info.SignalUsage}, \"{info.SignalName}\")");
                Exists(result, "condition", true, Elements(Get(function, "Conditions"), "Conditions").Count == count + 1, $"The new condition of safety function '{info.FunctionName}'", null);
                result.Items.Add($"condition position {Text(count + 1)}");
                if (info.Settings.Count > 0 && condition is not null) SetProperties(context, condition, "Condition", info.Settings, definition.Settings, result);
                return true;
            }

            case M.UpdateSafetyFunctionCondition:
            case M.DeleteSafetyFunctionCondition:
            {
                result.Target += $", safety function '{info.FunctionName}', condition {info.Position}";
                var function = FindFunction(context, testObject, info.FunctionName);
                var condition = FindCondition(function, info.Position);
                if (method == M.UpdateSafetyFunctionCondition)
                {
                    SetProperties(context, condition, "Condition", info.Settings, definition.Settings, result);
                    return true;
                }

                var count = Elements(Get(function, "Conditions"), "Conditions").Count;
                Delete(condition, "Condition.Delete()", result);
                Exists(result, "condition", false, Elements(Get(function, "Conditions"), "Conditions").Count == count, $"Condition {info.Position}", null);
                return true;
            }

            case M.ExportActivationTest:
            {
                var export = RequireMethodByShape(testObject, "Export", p => p.Length == 2 && p[0].ParameterType == typeof(FileInfo) && p[1].ParameterType.IsEnum, "ActivationTest.Export(FileInfo, ExportOptions)");
                var options = ConvertValue(export.GetParameters()[1].ParameterType, "None", "ExportOptions");
                result.File = WriteFile(context, info.FilePath, info.Overwrite, "ActivationTest.Export", temporary => Call(export, testObject, new FileInfo(temporary), options), result.Messages);
                result.Calls.Add("ActivationTest.Export(FileInfo, ExportOptions.None)");
                return true;
            }

            case M.GenerateActivationTestReport:
            {
                var printout = ChildService(context, testObject, N.TestPrintout) ?? ChildService(context, assistant, N.TestPrintout)
                    ?? throw new RuntimeBindingException(WorkerFailureCategories.UnsupportedCapability, $"Neither activation test '{info.TestName}' nor the assistant offers {N.TestPrintout}. Nothing was written.");
                var generate = RequireMethod(printout, "Generate", new[] { typeof(FileInfo) }, "ActivationTestPrintout.Generate(FileInfo)");
                result.File = WriteFile(context, info.FilePath, info.Overwrite, "ActivationTestPrintout.Generate", temporary => Call(generate, printout, new FileInfo(temporary)), result.Messages);
                result.Calls.Add("ActivationTestPrintout.Generate(FileInfo)");
                return true;
            }
        }

        return false;
    }

    private static void UpdateFunction(PlcSettingsContext context, object function, PlcSettingsRequestInfo info, PlcSettingsDefinition definition, PlcSettingsResultInfo result)
    {
        if (info.Settings.Count > 0) SetProperties(context, function, "SafetyFunction", info.Settings, definition.Settings, result);
        if (info.Traced is { } traced)
        {
            var trace = Get(function, "TraceConfiguration") ?? throw Missing("SafetyFunction.TraceConfiguration");
            SetProperties(context, trace, "TraceConfiguration", new[] { new PlcSettingValueInfo { Name = "IsTraced", Value = traced ? "true" : "false" } }, new[] { "IsTraced" }, result);
        }
    }
}
