using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Hmi;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.TechnologyObjects;
using TiaMcpServer.OpennessWorker.Worker;
using static TiaMcpServer.OpennessWorker.Openness.Hmi.HmiWriteRun;
using M = TiaMcpServer.Contracts.Hmi.HmiUnifiedConfigurationMethods;

namespace TiaMcpServer.OpennessWorker.Openness.HmiExtensions;

/// <summary>
/// WinCC Unified alarm audit classes, runtime settings and plant views/objects (issue #29). Every member is documented in
/// the V21 <c>Siemens.Engineering.WinCCUnified.xml</c>; those outside the compile stubs are bound at runtime by exact name
/// and argument types (<see cref="HmiUnifiedConfigurationMethods"/> lists them) and fail <c>api_member_missing</c> before
/// anything changes. V21 documents <c>Cpm.PlantViewsProvider</c> but not which object offers it: it is looked up by its
/// full type name among the services the HMI software and then the project list (<c>GetServiceInfos()</c>, as the
/// technology objects and the system diagnostics settings do). Property values go through
/// <see cref="HmiScreenWriter.SetValue"/> (converted to the runtime type of the current value, read back). Lives outside
/// <c>Openness/Hmi</c> because it uses the service helpers of <c>TechnologyObjectRuntime</c>; <c>HmiPitfallSourceTests</c>
/// scans this folder too.
/// </summary>
internal static class HmiUnifiedConfiguration
{
    private const int MaxNodes = 5000;
    private const int MaxDepth = 16;
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    // ---- plant views read ------------------------------------------------------------------------------

    public static HmiPlantViewsInfo ListPlantViews(ProjectBase project, HmiSoftware software, HmiQueryInfo query)
    {
        var (provider, owner) = Provider(project, software);
        var info = new HmiPlantViewsInfo { ProviderOwner = owner };
        foreach (var view in Items(Value(provider, "PlantViews")!))
        {
            var name = Text(view, "Name");
            if (query.PlantViewName is not null && !Names.Equals(name, query.PlantViewName)) continue;
            var row = new HmiPlantViewInfo { Name = name, AssignedHmiDevice = Optional(view, "AssignedHmiDevice", info.Messages) };
            var count = 0;
            void Walk(object parent, string? path, int depth)
            {
                foreach (var node in Items(Value(parent, "PlantViewNodes")!))
                {
                    if (count >= MaxNodes || depth > MaxDepth) { row.NodesTruncated = true; return; }
                    count++;
                    var nodeName = Text(node, "Name");
                    var nodePath = path is null ? nodeName : path + "/" + nodeName;
                    var nodeRow = new HmiPlantViewNodeInfo { Name = nodeName, NodePath = nodePath, ViewPath = Optional(node, "ViewPath", info.Messages) };
                    if (Safe(() => Value(node, "PlantObject"), info.Messages, $"PlantObject of '{nodePath}'") is { } plantObject)
                    {
                        nodeRow.PlantObjectType = Optional(plantObject, "Type", info.Messages);
                        if (Safe(() => Value(plantObject, "PlantObjectInterfaces"), info.Messages, $"PlantObjectInterfaces of '{nodePath}'") is { } interfaces)
                            nodeRow.Interfaces = Items(interfaces).Select(i => Text(i, "Name")).ToList();
                    }

                    row.Nodes.Add(nodeRow);
                    Walk(node, nodePath, depth + 1);
                }
            }

            Walk(view, null, 0);
            info.PlantViews.Add(row);
        }

        if (query.PlantViewName is not null && info.PlantViews.Count == 0)
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No plant view '{query.PlantViewName}' was found.");
        info.IsComplete = info.Messages.Count == 0 && info.PlantViews.All(v => !v.NodesTruncated);
        return info;
    }

    // ---- writes ------------------------------------------------------------------------------------------

    public static HmiWriteOutcomeInfo Write(ProjectBase project, HmiSoftware software, string method, HmiWriteRequestInfo write)
        => method switch
        {
            M.CreateAuditClass => CreateAuditClass(software, write),
            M.DeleteAuditClass => DeleteAuditClass(software, write),
            M.SetAuditClassProperty => SetAuditClassProperty(software, write),
            M.SetRuntimeSetting => SetRuntimeSetting(software, write),
            M.CreatePlantView => CreatePlantView(project, software, write),
            M.DeletePlantView => DeletePlantView(project, software, write),
            M.CreatePlantViewNode => CreatePlantViewNode(project, software, write),
            M.DeletePlantViewNode => DeletePlantViewNode(project, software, write),
            M.SetPlantObjectInterfaceProperty => SetPlantObjectInterfaceProperty(project, software, write),
            M.ResizeScreen => ResizeScreen(software, write),
            M.SetLogDuration => SetLogDuration(software, write),
            M.SetOpcUaAlarmTypeNode => SetOpcUaAlarmTypeNode(software, write),
            _ => throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"Unsupported HMI worker method '{method}'."),
        };

    private static HmiWriteOutcomeInfo CreateAuditClass(HmiSoftware software, HmiWriteRequestInfo write)
    {
        var name = HmiScreenWriter.Required(write.AuditClassName, "auditClassName");
        var composition = AuditClasses(software);
        var create = HmiScreenWriter.Bind(composition, "Create", Type.EmptyTypes);
        var find = HmiScreenWriter.Bind(composition, "Find", new[] { typeof(string) });
        var outcome = new HmiWriteOutcomeInfo { ObjectName = name };
        IEngineeringObject? created = null;
        try
        {
            Set(outcome, "Create", () => created = (IEngineeringObject?)HmiScreenWriter.Invoke(create, composition)
                ?? throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, "HmiAlarmAuditClassComposition.Create() returned no audit class."));
            Set(outcome, "Name", () => HmiScreenWriter.SetValue(created!, "Name", name, null));
            if (HmiScreenWriter.Invoke(find, composition, name) is null)
                throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"Find(\"{name}\") finds no audit class after the create.");
            outcome.AppliedProperties.Add("readBack present");
            return outcome;
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException)
        {
            outcome.Cleanup = HmiCleanupStates.NotNeeded;
            if (created is not null)
            {
                try
                {
                    HmiScreenWriter.Invoke(HmiScreenWriter.Bind(created, "Delete", Type.EmptyTypes), created);
                    outcome.Cleanup = HmiCleanupStates.Deleted;
                }
                catch (Exception cleanup) when (cleanup is EngineeringException or WorkerOperationException)
                {
                    outcome.Cleanup = HmiCleanupStates.Failed;
                    outcome.CleanupMessage = cleanup.Message;
                }
            }

            throw Failure(ex, outcome);
        }
    }

    private static HmiWriteOutcomeInfo DeleteAuditClass(HmiSoftware software, HmiWriteRequestInfo write)
    {
        var (composition, find, auditClass) = AuditClass(software, write);
        var delete = HmiScreenWriter.Bind(auditClass, "Delete", Type.EmptyTypes);
        var outcome = new HmiWriteOutcomeInfo { ObjectName = write.AuditClassName! };
        try
        {
            Set(outcome, "Delete", () => HmiScreenWriter.Invoke(delete, auditClass));
            if (HmiScreenWriter.Invoke(find, composition, write.AuditClassName!) is not null)
                throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"Audit class '{write.AuditClassName}' is still found after Delete().");
            outcome.AppliedProperties.Add("readBack gone");
            return outcome;
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException) { throw Failure(ex, outcome); }
    }

    private static HmiWriteOutcomeInfo SetAuditClassProperty(HmiSoftware software, HmiWriteRequestInfo write)
    {
        var (_, _, auditClass) = AuditClass(software, write);
        return SetProperty(auditClass, write, write.AuditClassName!);
    }

    private static HmiWriteOutcomeInfo SetRuntimeSetting(HmiSoftware software, HmiWriteRequestInfo write)
    {
        object settings = software.RuntimeSettings
            ?? throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, "The runtime settings of the HMI are not available.");
        var described = "runtime settings";
        if (write.SettingsSection is { } section)
        {
            settings = Value(settings, section)
                ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"The runtime settings section '{section}' is not available on this HMI.");
            described = $"runtime settings {section}";
        }

        if (settings is not IEngineeringObject target)
            throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
                $"The {described} value is a {settings.GetType().Name}, not a settings object; set it as propertyName on the runtime settings (no settingsSection). Nothing was changed.");
        return SetProperty(target, write, described);
    }

    private static HmiWriteOutcomeInfo CreatePlantView(ProjectBase project, HmiSoftware software, HmiWriteRequestInfo write)
    {
        var name = HmiScreenWriter.Required(write.PlantViewName, "plantViewName");
        var views = Value(Provider(project, software).Provider, "PlantViews")!;
        var create = HmiScreenWriter.Bind(views, "Create", new[] { typeof(string) });
        var find = HmiScreenWriter.Bind(views, "Find", new[] { typeof(string) });
        var outcome = new HmiWriteOutcomeInfo { ObjectName = name };
        try
        {
            Set(outcome, "Create", () => HmiScreenWriter.Invoke(create, views, name));
            if (HmiScreenWriter.Invoke(find, views, name) is null)
                throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"Find(\"{name}\") finds no plant view after the create.");
            outcome.AppliedProperties.Add("readBack present");
            return outcome;
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException) { throw Failure(ex, outcome); }
    }

    private static HmiWriteOutcomeInfo DeletePlantView(ProjectBase project, HmiSoftware software, HmiWriteRequestInfo write)
    {
        var (views, find, view) = View(project, software, write);
        var delete = HmiScreenWriter.Bind(view, "Delete", Type.EmptyTypes);
        var outcome = new HmiWriteOutcomeInfo { ObjectName = write.PlantViewName! };
        try
        {
            Set(outcome, "Delete", () => HmiScreenWriter.Invoke(delete, view));
            if (HmiScreenWriter.Invoke(find, views, write.PlantViewName!) is not null)
                throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"Plant view '{write.PlantViewName}' is still found after Delete().");
            outcome.AppliedProperties.Add("readBack gone");
            return outcome;
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException) { throw Failure(ex, outcome); }
    }

    private static HmiWriteOutcomeInfo CreatePlantViewNode(ProjectBase project, HmiSoftware software, HmiWriteRequestInfo write)
    {
        var (_, _, view) = View(project, software, write);
        var name = HmiScreenWriter.Required(write.NodeName, "nodeName");
        var parent = write.NodePath is null ? view : Node(view, write.NodePath);
        var nodes = Value(parent, "PlantViewNodes")!;
        var create = write.PlantObjectType is null
            ? HmiScreenWriter.Bind(nodes, "Create", new[] { typeof(string) })
            : HmiScreenWriter.Bind(nodes, "Create", new[] { typeof(string), typeof(string) });
        var path = write.NodePath is null ? name : write.NodePath + "/" + name;
        var outcome = new HmiWriteOutcomeInfo { ObjectName = $"{write.PlantViewName}/{path}" };
        try
        {
            Set(outcome, "Create", () =>
            {
                if (write.PlantObjectType is null) HmiScreenWriter.Invoke(create, nodes, name);
                else HmiScreenWriter.Invoke(create, nodes, name, write.PlantObjectType);
            });
            if (!Items(nodes).Any(n => Names.Equals(Text(n, "Name"), name)))
                throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"The plant view node '{path}' does not appear after the create.");
            outcome.AppliedProperties.Add("readBack present");
            return outcome;
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException) { throw Failure(ex, outcome); }
    }

    private static HmiWriteOutcomeInfo DeletePlantViewNode(ProjectBase project, HmiSoftware software, HmiWriteRequestInfo write)
    {
        var (_, _, view) = View(project, software, write);
        var path = HmiScreenWriter.Required(write.NodePath, "nodePath");
        var segments = path.Split('/');
        var parent = segments.Length == 1 ? view : Node(view, string.Join("/", segments.Take(segments.Length - 1)));
        var node = Node(view, path);
        var delete = HmiScreenWriter.Bind(node, "Delete", Type.EmptyTypes);
        var outcome = new HmiWriteOutcomeInfo { ObjectName = $"{write.PlantViewName}/{path}" };
        try
        {
            Set(outcome, "Delete", () => HmiScreenWriter.Invoke(delete, node));
            if (Items(Value(parent, "PlantViewNodes")!).Any(n => Names.Equals(Text(n, "Name"), segments[segments.Length - 1])))
                throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"The plant view node '{path}' is still present after Delete().");
            outcome.AppliedProperties.Add("readBack gone");
            return outcome;
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException) { throw Failure(ex, outcome); }
    }

    private static HmiWriteOutcomeInfo SetPlantObjectInterfaceProperty(ProjectBase project, HmiSoftware software, HmiWriteRequestInfo write)
    {
        var (_, _, view) = View(project, software, write);
        var path = HmiScreenWriter.Required(write.NodePath, "nodePath");
        var plantObject = Value(Node(view, path), "PlantObject")
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"The plant view node '{path}' has no plant object.");
        var name = HmiScreenWriter.Required(write.InterfaceName, "interfaceName");
        var matches = Items(Value(plantObject, "PlantObjectInterfaces")!).Where(i => Names.Equals(Text(i, "Name"), name)).ToList();
        if (matches.Count != 1)
            throw new WorkerOperationException(matches.Count == 0 ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous,
                $"{matches.Count} plant object interfaces of '{path}' are named '{name}'.");
        if (matches[0] is not IEngineeringObject target)
            throw HmiScreenWriter.Missing($"The plant object interface '{name}' is not an engineering object.");
        return SetProperty(target, write, $"{write.PlantViewName}/{path} interface {name}");
    }

    // ---- helpers --------------------------------------------------------------------------------------

    private static HmiWriteOutcomeInfo SetProperty(IEngineeringObject target, HmiWriteRequestInfo write, string described)
    {
        var property = HmiScreenWriter.Required(write.PropertyName, "propertyName");
        var outcome = new HmiWriteOutcomeInfo { ObjectName = described };
        try
        {
            Set(outcome, property, () => HmiScreenWriter.SetValue(target, property, write.Value ?? string.Empty, write.Culture));
            return outcome;
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException) { throw Failure(ex, outcome); }
    }

    private static object AuditClasses(HmiSoftware software) => software.HmiAlarmAuditClass;

    private static (object Composition, MethodInfo Find, IEngineeringObject AuditClass) AuditClass(HmiSoftware software, HmiWriteRequestInfo write)
    {
        var name = HmiScreenWriter.Required(write.AuditClassName, "auditClassName");
        var composition = AuditClasses(software);
        var find = HmiScreenWriter.Bind(composition, "Find", new[] { typeof(string) });
        var found = HmiScreenWriter.Invoke(find, composition, name) as IEngineeringObject
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No alarm audit class '{name}' was found. Use hmi_read list_alarm_classes.");
        return (composition, find, found);
    }

    private static (object Views, MethodInfo Find, object View) View(ProjectBase project, HmiSoftware software, HmiWriteRequestInfo write)
    {
        var name = HmiScreenWriter.Required(write.PlantViewName, "plantViewName");
        var views = Value(Provider(project, software).Provider, "PlantViews")!;
        var find = HmiScreenWriter.Bind(views, "Find", new[] { typeof(string) });
        var view = HmiScreenWriter.Invoke(find, views, name)
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No plant view '{name}' was found. Use hmi_read list_plant_views.");
        return (views, find, view);
    }

    /// <summary>The node at the slash-separated <paramref name="path"/> of node names below <paramref name="view"/>.</summary>
    private static object Node(object view, string path)
    {
        var current = view;
        var walked = new List<string>();
        foreach (var segment in path.Split('/'))
        {
            walked.Add(segment);
            var matches = Items(Value(current, "PlantViewNodes")!).Where(n => Names.Equals(Text(n, "Name"), segment)).ToList();
            current = matches.Count switch
            {
                1 => matches[0],
                0 => throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"The plant view node '{string.Join("/", walked)}' was not found."),
                _ => throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous, $"{matches.Count} plant view nodes match '{string.Join("/", walked)}'."),
            };
        }

        return current;
    }

    /// <summary>The PlantViewsProvider among the listed services of the HMI software, then of the project.</summary>
    private static (object Provider, string Owner) Provider(ProjectBase project, HmiSoftware software)
    {
        var reasons = new List<string>();
        foreach (var (owner, label) in new (object, string)[] { (software, "hmiSoftware"), (project, "project") })
        {
            IReadOnlyList<Type> types;
            try
            {
                types = TechnologyObjectRuntime.ServiceTypes(owner);
            }
            catch (WorkerOperationException exception) when (exception.FailureCategory == WorkerFailureCategories.UnsupportedCapability)
            {
                reasons.Add($"{label}: {exception.Message}");
                continue;
            }

            if (TechnologyObjectRuntime.TryService(owner, types, M.PlantViewsProviderType, out var unavailable) is { } provider) return (provider, label);
            reasons.Add(unavailable is null ? $"{label}: not listed" : $"{label}: {unavailable}");
        }

        throw new WorkerOperationException(WorkerFailureCategories.ApiMemberMissing,
            $"No {M.PlantViewsProviderType} was found among the services of the HMI software or the project ({string.Join("; ", reasons)}). V21 documents the type but not its owner; plant views need the WinCC Unified plant model option. Nothing was changed.");
    }

    // ---- package G ---------------------------------------------------------------------------------------

    private static HmiWriteOutcomeInfo ResizeScreen(HmiSoftware software, HmiWriteRequestInfo write)
    {
        var screen = HmiScreenWriter.Screen(software, write.ScreenName, write.GroupPath);
        var resize = HmiScreenWriter.Bind(screen, "ResizeScreen", Type.EmptyTypes);
        var outcome = new HmiWriteOutcomeInfo { ObjectName = write.ScreenName! };
        var messages = new List<string>();
        var before = $"{Optional(screen, "Width", messages) ?? "?"}x{Optional(screen, "Height", messages) ?? "?"}";
        try
        {
            Set(outcome, "ResizeScreen", () => HmiScreenWriter.Invoke(resize, screen));
            outcome.AppliedProperties.Add($"size {before} -> {Optional(screen, "Width", messages) ?? "?"}x{Optional(screen, "Height", messages) ?? "?"}");
            return outcome;
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException) { throw Failure(ex, outcome); }
    }

    private static HmiWriteOutcomeInfo SetLogDuration(HmiSoftware software, HmiWriteRequestInfo write)
    {
        var name = HmiScreenWriter.Required(write.ObjectName, "objectName");
        var parts = HmiScreenWriter.Required(write.PropertyName, "propertyName").Split('.');
        var logs = Value(software, parts[0] == "AlarmLog" ? "AlarmLogs" : "DataLogs")
            ?? throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, $"HmiSoftware.{parts[0]}s returned null.");
        var log = Items(logs).FirstOrDefault(l => Text(l, "Name") == name)
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No {parts[0]} '{name}' was found. Nothing was changed.");
        var segment = parts[1] == "SegmentTimePeriod";
        var owner = Value(log, segment ? "Segment" : "Settings")
            ?? throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, $"{parts[0]} '{name}' has no {(segment ? "Segment" : "Settings")}.");
        var duration = Value(owner, parts[1])
            ?? throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, $"{parts[0]} '{name}' has no {parts[1]}.");
        var numbers = HmiScreenWriter.Required(write.Value, "value").Split(':').Select(p => uint.Parse(p, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        while (numbers.Count < 5) numbers.Add(0);
        var setter = HmiScreenWriter.Bind(duration, segment ? "SetSegmentDuration" : "SetLogDuration", new[] { typeof(uint), typeof(uint), typeof(uint), typeof(uint), typeof(uint) });
        var reader = HmiScreenWriter.Bind(duration, segment ? "GetStringSegmentDuration" : "GetStringLogDuration", Type.EmptyTypes);
        var outcome = new HmiWriteOutcomeInfo { ObjectName = name };
        try
        {
            var before = HmiScreenWriter.Invoke(reader, duration)?.ToString();
            Set(outcome, segment ? "SetSegmentDuration" : "SetLogDuration", () => HmiScreenWriter.Invoke(setter, duration, numbers.Cast<object>().ToArray()));
            var parts2 = new[] { "Days", "Hours", "Minutes", "Seconds", "Ticks" };
            for (var index = 0; index < parts2.Length; index++)
                if (Value(duration, parts2[index]) is uint actual && actual != numbers[index])
                    throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                        $"{parts[1]}.{parts2[index]} of {parts[0]} '{name}' reads {actual} after the set (requested {numbers[index]}).");
            outcome.AppliedProperties.Add($"readBack {before} -> {HmiScreenWriter.Invoke(reader, duration)}");
            return outcome;
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException) { throw Failure(ex, outcome); }
    }

    private static HmiWriteOutcomeInfo SetOpcUaAlarmTypeNode(HmiSoftware software, HmiWriteRequestInfo write)
    {
        var name = HmiScreenWriter.Required(write.ObjectName, "objectName");
        var types = Value(software, "OpcUaAlarmTypes") ?? throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, "HmiSoftware.OpcUaAlarmTypes returned null.");
        var alarmType = Items(types).FirstOrDefault(t => Text(t, "Name") == name)
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No OPC UA alarm type '{name}' was found. Nothing was changed.");
        var set = HmiScreenWriter.Bind(alarmType, "SetNodeIdAndConnection", new[] { typeof(string), typeof(string) });
        var outcome = new HmiWriteOutcomeInfo { ObjectName = name };
        try
        {
            Set(outcome, "SetNodeIdAndConnection", () => HmiScreenWriter.Invoke(set, alarmType, write.Value!, write.Connection!));
            var messages = new List<string>();
            var node = Optional(alarmType, "NodeId", messages);
            var connection = Optional(alarmType, "Connection", messages);
            if (node is not null && node != write.Value)
                throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"NodeId of OPC UA alarm type '{name}' reads '{node}' after the set.");
            outcome.AppliedProperties.Add(node is null ? "readBack unavailable (no NodeId property)" : $"readBack NodeId {node}, Connection {connection}");
            return outcome;
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException) { throw Failure(ex, outcome); }
    }

    private static object? Value(object owner, string name)
    {
        var property = LibraryRuntimeBinding.Property(owner.GetType(), name, out var error)
            ?? throw HmiScreenWriter.Missing($"{owner.GetType().Name}.{name}: {error}");
        try
        {
            return property.GetValue(owner);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static IEnumerable<object> Items(object composition)
        => composition is IEnumerable items ? items.Cast<object>().Where(i => i is not null).ToList()
            : throw HmiScreenWriter.Missing($"{composition.GetType().Name} is not an enumerable composition.");

    private static string Text(object owner, string name) => Value(owner, name)?.ToString() ?? string.Empty;

    private static string? Optional(object owner, string name, List<string> messages)
        => Safe(() => LibraryRuntimeBinding.Property(owner.GetType(), name, out _) is null ? null : HmiScreenEvents.Text(Value(owner, name)), messages, $"{owner.GetType().Name}.{name}");

    private static T? Safe<T>(Func<T?> read, List<string> messages, string what) where T : class
    {
        try
        {
            return read();
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
        {
            messages.Add($"{what} could not be read: {exception.Message}");
            return null;
        }
    }
}
