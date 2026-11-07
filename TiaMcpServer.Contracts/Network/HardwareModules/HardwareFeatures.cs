using System.Collections;
using System.Reflection;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.PlcAlarms;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Contracts.Network;

/// <summary>
/// Package G (V21 API completion; not live-tested): further hardware features of a device item. Evidence: Siemens.Engineering.Base.xml
/// (V21): <c>HW.Node.CreateAndConnectToSubnet(String)</c> (returns the Subnet), <c>.DisconnectFromSubnet()</c>, <c>.ConnectedSubnet</c>;
/// <c>HW.Features.NetworkInterface.Nodes</c>, <c>.IoConnectors</c>; <c>HW.IoConnector.GetIoController()</c>, <c>HW.IoController.IoSystem</c>;
/// <c>HW.Features.ImConnection.GetPartnerPort()</c>, <c>.Connect(DeviceItem)</c>, <c>.Disconnect()</c>; <c>HW.Features.PcInterfaceAssignment
/// .AssignInterface(PcInterfaceAssignmentMode)</c>, <c>.AssignInterface(PcInterfaceAssignmentMode, DeviceItem)</c>, <c>.GetAvailableIPCExpansions()</c>,
/// <c>.PcInterfaceAssignmentMode</c>, <c>.HardwareResource</c>, <c>.IpcExpansion</c>, <c>.SoftwarePlc</c> (enum PcInterfaceAssignmentMode None, PcStation,
/// SoftwarePlc); <c>HW.Features.ModuleDescriptionUpdater.CanUpdate</c>, <c>.UpdateModuleDescription()</c> (Boolean);
/// <c>HW.Features.TelecontrolManagement.ExportDataPoints(FileInfo)</c>, <c>.ImportDataPoints(FileInfo)</c> (a csv file), <c>.TelecontrolDataPoints</c>;
/// <c>HW.Features.CommunicationManagement.Connections</c>, <c>CommunicationConnections.ConnectionComposition.Create&lt;T&gt;(Node, DeviceItem, Node)</c>
/// with T one of the documented connection types, <c>Connection.ConnectionType</c>, <c>.IsValid</c>, <c>.LocalSubnetName</c>, <c>.PartnerSubnetName</c>,
/// <c>.LocalTarget</c>, <c>.PartnerTarget</c>, <c>.Delete()</c>. The features are found among the device item's <c>GetServiceInfos()</c> by exact full
/// name; every member is bound at runtime by exact name and parameter types (no stub member).
/// </summary>
public static class HardwareFeatureMethods
{
    public const string GetNodes = "get_interface_nodes";
    public const string CreateAndConnectSubnet = "create_subnet_and_connect_node";
    public const string DisconnectFromSubnet = "disconnect_node_from_subnets";
    public const string GetIoController = "get_io_controller";
    public const string GetImPartnerPort = "get_im_partner_port";
    public const string ConnectImPort = "connect_im_port";
    public const string DisconnectImPort = "disconnect_im_port";
    public const string GetPcInterfaceAssignment = "get_pc_interface_assignment";
    public const string AssignPcInterface = "assign_pc_interface";
    public const string GetModuleDescriptionUpdate = "get_module_description_update";
    public const string UpdateModuleDescription = "update_module_description";
    public const string ExportTelecontrolDataPoints = "export_telecontrol_data_points";
    public const string ImportTelecontrolDataPoints = "import_telecontrol_data_points";
    public const string ListCommunicationConnections = "list_communication_connections";
    public const string CreateCommunicationConnection = "create_communication_connection";
    public const string DeleteCommunicationConnection = "delete_communication_connection";

    public static IReadOnlyList<string> Read { get; } = new[]
    {
        GetNodes, GetIoController, GetImPartnerPort, GetPcInterfaceAssignment, GetModuleDescriptionUpdate, ListCommunicationConnections,
    };

    public static IReadOnlyList<string> Write { get; } = new[]
    {
        CreateAndConnectSubnet, DisconnectFromSubnet, ConnectImPort, DisconnectImPort, AssignPcInterface, UpdateModuleDescription,
        ExportTelecontrolDataPoints, ImportTelecontrolDataPoints, CreateCommunicationConnection, DeleteCommunicationConnection,
    };

    public static bool IsFeature(string? method) => method is not null && (Read.Contains(method, StringComparer.Ordinal) || Write.Contains(method, StringComparer.Ordinal));
}

public static class HardwareFeatureNames
{
    public const string NetworkInterface = "Siemens.Engineering.HW.Features.NetworkInterface";
    public const string ImConnection = "Siemens.Engineering.HW.Features.ImConnection";
    public const string PcInterfaceAssignment = "Siemens.Engineering.HW.Features.PcInterfaceAssignment";
    public const string ModuleDescriptionUpdater = "Siemens.Engineering.HW.Features.ModuleDescriptionUpdater";
    public const string TelecontrolManagement = "Siemens.Engineering.HW.Features.TelecontrolManagement";
    public const string CommunicationManagement = "Siemens.Engineering.HW.Features.CommunicationManagement";
    public const string ConnectionNamespace = "Siemens.Engineering.HW.CommunicationConnections";

    /// <summary>The documented connection types of <c>ConnectionComposition.Create&lt;T&gt;</c> (namespace <see cref="ConnectionNamespace"/>).</summary>
    public static readonly string[] ConnectionTypes =
    {
        "S7Connection", "HmiConnection", "TcpConnection", "UdpConnection", "IsoOnTcpConnection", "IsoConnection", "FdlConnection", "PtpConnection",
    };

    public static readonly string[] AssignmentModes = { "None", "PcStation", "SoftwarePlc" };

    public const int MaxConnections = 500;
}

/// <summary>One value of a hardware feature result.</summary>
public sealed class HardwareFeatureValueInfo
{
    public string Name { get; set; } = string.Empty;

    public RuntimeValueInfo Value { get; set; } = new();
}

/// <summary>The result of a package G hardware feature read or write.</summary>
public sealed class HardwareFeatureResultInfo
{
    public string Operation { get; set; } = string.Empty;

    public HardwareObjectIdentityInfo Target { get; set; } = new();

    /// <summary>The feature (service) full name used.</summary>
    public string Service { get; set; } = string.Empty;

    /// <summary>The bound members called, in order.</summary>
    public List<string> Calls { get; set; } = new();

    public List<HardwareFeatureValueInfo> Values { get; set; } = new();

    /// <summary>Objects the read or write names (nodes, partner ports, IO controllers, connections), described.</summary>
    public List<string> Items { get; set; } = new();

    /// <summary>A write's read-back: matched, notAvailable (nothing documented reads it back), unreadable.</summary>
    public string ReadBack { get; set; } = "notAvailable";

    /// <summary>The file written (export) or read (import); null otherwise.</summary>
    public string? FilePath { get; set; }

    public long? FileBytes { get; set; }

    public string? FileSha256 { get; set; }

    public List<string> Messages { get; set; } = new();
}

/// <summary>What the Siemens-free engine sees of the target (live objects from the worker, stand-ins from the FakeWorker).</summary>
public sealed class HardwareFeatureContext
{
    public object Target { get; set; } = new();

    public HardwareObjectIdentityInfo TargetIdentity { get; set; } = new();

    /// <summary>The partner device item (IM partner port, the software PLC CPU, the connection's partner target), null when none was given.</summary>
    public object? Partner { get; set; }

    public Func<object, IReadOnlyList<Type>> ServiceTypes { get; set; } = _ => Array.Empty<Type>();

    public Func<object, Type, object?> Service { get; set; } = (_, _) => null;

    /// <summary>A short description of a live hardware object (device item name and device), for results.</summary>
    public Func<object?, string> Describe { get; set; } = value => value?.GetType().Name ?? "(null)";

    public Func<Exception, bool> Recoverable { get; set; } = _ => false;

    public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;
}

/// <summary>
/// The Siemens-free engine of the package G hardware features, shared by the worker and the FakeWorker. Refusals are
/// <see cref="RuntimeBindingException"/> with their category; a write that reads back differently is postcondition_failed.
/// </summary>
public static class HardwareFeatureRun
{
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

    public static HardwareFeatureResultInfo Run(string method, HardwareFeatureContext context, HardwareModuleRequestInfo request)
    {
        var result = new HardwareFeatureResultInfo { Operation = method, Target = context.TargetIdentity };
        switch (method)
        {
            case HardwareFeatureMethods.GetNodes:
            {
                var nodes = Nodes(context, result);
                foreach (var node in nodes)
                {
                    var subnet = Get(node, "ConnectedSubnet");
                    result.Items.Add($"node '{Name(node)}': connected subnet {(subnet is null ? "(none)" : $"'{Name(subnet)}'")}");
                }

                return result;
            }
            case HardwareFeatureMethods.CreateAndConnectSubnet:
            case HardwareFeatureMethods.DisconnectFromSubnet:
            {
                var node = Node(context, request, result);
                var before = Get(node, "ConnectedSubnet");
                result.Values.Add(Value("connectedSubnetBefore", before is null ? null : Name(before)));
                if (method == HardwareFeatureMethods.CreateAndConnectSubnet)
                {
                    var name = request.NewSubnetName ?? throw Validation("create_subnet_and_connect_node needs newSubnetName.");
                    var create = Method(node, "CreateAndConnectToSubnet", new[] { typeof(string) }, "Node.CreateAndConnectToSubnet(String)");
                    var created = Call(create, node, name);
                    result.Calls.Add($"Node.CreateAndConnectToSubnet(\"{name}\")");
                    var after = Get(node, "ConnectedSubnet");
                    result.Values.Add(Value("connectedSubnetAfter", after is null ? null : Name(after)));
                    if (after is null || !Equals(after, created) && Name(after) != name)
                        throw new RuntimeBindingException(WorkerFailureCategories.PostconditionFailed,
                            $"After CreateAndConnectToSubnet(\"{name}\") the node is connected to {(after is null ? "no subnet" : $"'{Name(after)}'")}. Inspect it with network_read read_hardware_config.");
                }
                else
                {
                    var disconnect = Method(node, "DisconnectFromSubnet", Type.EmptyTypes, "Node.DisconnectFromSubnet()");
                    Call(disconnect, node);
                    result.Calls.Add("Node.DisconnectFromSubnet()");
                    var after = Get(node, "ConnectedSubnet");
                    result.Values.Add(Value("connectedSubnetAfter", after is null ? null : Name(after)));
                    if (after is not null)
                        throw new RuntimeBindingException(WorkerFailureCategories.PostconditionFailed,
                            $"After DisconnectFromSubnet() the node is still connected to '{Name(after)}'. Inspect it with network_read read_hardware_config.");
                }

                result.ReadBack = "matched";
                return result;
            }
            case HardwareFeatureMethods.GetIoController:
            {
                var networkInterface = Feature(context, HardwareFeatureNames.NetworkInterface, result);
                foreach (var connector in Elements(Get(networkInterface, "IoConnectors")))
                {
                    var get = Method(connector!, "GetIoController", Type.EmptyTypes, "IoConnector.GetIoController()");
                    var controller = Call(get, connector!);
                    var system = controller is null ? null : TryGet(context, controller, "IoSystem");
                    result.Items.Add($"IO connector: IO controller {(controller is null ? "(none)" : context.Describe(Owner(controller)))}"
                        + (system is null ? string.Empty : $", IO system '{Name(system)}'"));
                }

                result.Calls.Add("IoConnector.GetIoController()");
                if (result.Items.Count == 0) result.Messages.Add("The interface has no IO connector (it is no IO device of an IO system).");
                return result;
            }
            case HardwareFeatureMethods.GetImPartnerPort:
            case HardwareFeatureMethods.ConnectImPort:
            case HardwareFeatureMethods.DisconnectImPort:
            {
                var im = Feature(context, HardwareFeatureNames.ImConnection, result);
                var getPartner = Method(im, "GetPartnerPort", Type.EmptyTypes, "ImConnection.GetPartnerPort()");
                var before = Call(getPartner, im);
                result.Values.Add(Value("partnerPortBefore", before is null ? null : context.Describe(before)));
                if (method == HardwareFeatureMethods.GetImPartnerPort)
                {
                    result.Calls.Add("ImConnection.GetPartnerPort()");
                    return result;
                }

                if (method == HardwareFeatureMethods.ConnectImPort)
                {
                    var partner = context.Partner ?? throw Validation("connect_im_port needs partner (the partner port's device item).");
                    var connect = MethodByShape(im, "Connect", p => p.Length == 1 && p[0].ParameterType.IsInstanceOfType(partner), "ImConnection.Connect(DeviceItem)");
                    Call(connect, im, partner);
                    result.Calls.Add("ImConnection.Connect(DeviceItem)");
                }
                else
                {
                    Call(Method(im, "Disconnect", Type.EmptyTypes, "ImConnection.Disconnect()"), im);
                    result.Calls.Add("ImConnection.Disconnect()");
                }

                var after = Call(getPartner, im);
                result.Values.Add(Value("partnerPortAfter", after is null ? null : context.Describe(after)));
                var ok = method == HardwareFeatureMethods.ConnectImPort ? after is not null && Equals(after, context.Partner) : after is null;
                if (!ok)
                    throw new RuntimeBindingException(WorkerFailureCategories.PostconditionFailed,
                        $"GetPartnerPort() reads {(after is null ? "no partner" : context.Describe(after))} after {method}. Inspect it with network_read get_im_partner_port.");
                result.ReadBack = "matched";
                return result;
            }
            case HardwareFeatureMethods.GetPcInterfaceAssignment:
            case HardwareFeatureMethods.AssignPcInterface:
            {
                var assignment = Feature(context, HardwareFeatureNames.PcInterfaceAssignment, result);
                if (method == HardwareFeatureMethods.AssignPcInterface)
                {
                    var mode = request.AssignmentMode ?? throw Validation("assign_pc_interface needs assignmentMode.");
                    MethodInfo assign;
                    object?[] arguments;
                    if (context.Partner is { } cpu)
                    {
                        assign = MethodByShape(assignment, "AssignInterface", p => p.Length == 2 && p[0].ParameterType.IsEnum && p[1].ParameterType.IsInstanceOfType(cpu),
                            "PcInterfaceAssignment.AssignInterface(PcInterfaceAssignmentMode, DeviceItem)");
                        arguments = new[] { EnumValue(assign.GetParameters()[0].ParameterType, mode), cpu };
                    }
                    else
                    {
                        assign = MethodByShape(assignment, "AssignInterface", p => p.Length == 1 && p[0].ParameterType.IsEnum, "PcInterfaceAssignment.AssignInterface(PcInterfaceAssignmentMode)");
                        arguments = new[] { EnumValue(assign.GetParameters()[0].ParameterType, mode) };
                    }

                    result.Values.Add(Value("modeBefore", TryGet(context, assignment, "PcInterfaceAssignmentMode")?.ToString()));
                    Call(assign, assignment, arguments);
                    result.Calls.Add(context.Partner is null ? $"AssignInterface({mode})" : $"AssignInterface({mode}, DeviceItem)");
                    var after = TryGet(context, assignment, "PcInterfaceAssignmentMode")?.ToString();
                    if (after is not null && after != mode)
                        throw new RuntimeBindingException(WorkerFailureCategories.PostconditionFailed,
                            $"PcInterfaceAssignmentMode reads '{after}' after AssignInterface({mode}). Inspect it with network_read get_pc_interface_assignment.");
                    result.ReadBack = after is null ? "unreadable" : "matched";
                }

                foreach (var property in new[] { "PcInterfaceAssignmentMode", "HardwareResource", "IpcExpansion", "SoftwarePlc" })
                    result.Values.Add(new HardwareFeatureValueInfo
                    {
                        Name = property,
                        Value = property == "SoftwarePlc" && TryGet(context, assignment, property) is { } plc ? RuntimeServiceValues.Project(context.Describe(plc))
                            : RuntimeServiceValues.ReadProperty(assignment, property, context.Recoverable),
                    });
                try
                {
                    var expansions = Call(Method(assignment, "GetAvailableIPCExpansions", Type.EmptyTypes, "PcInterfaceAssignment.GetAvailableIPCExpansions()"), assignment);
                    result.Values.Add(Value("availableIpcExpansions", string.Join(", ", Elements(expansions).Select(e => e?.ToString()))));
                }
                catch (Exception exception) when (context.Recoverable(exception))
                {
                    result.Messages.Add($"GetAvailableIPCExpansions() failed ({exception.GetType().Name}: {RuntimeServiceValues.Bound(exception.Message)}).");
                }

                return result;
            }
            case HardwareFeatureMethods.GetModuleDescriptionUpdate:
            case HardwareFeatureMethods.UpdateModuleDescription:
            {
                var updater = Feature(context, HardwareFeatureNames.ModuleDescriptionUpdater, result);
                result.Values.Add(new HardwareFeatureValueInfo { Name = "CanUpdate", Value = RuntimeServiceValues.ReadProperty(updater, "CanUpdate", context.Recoverable) });
                if (method == HardwareFeatureMethods.GetModuleDescriptionUpdate) return result;
                var updated = Call(Method(updater, "UpdateModuleDescription", Type.EmptyTypes, "ModuleDescriptionUpdater.UpdateModuleDescription()"), updater);
                result.Calls.Add("ModuleDescriptionUpdater.UpdateModuleDescription()");
                result.Values.Add(Value("updated", updated?.ToString()));
                if (updated is false)
                    throw new RuntimeBindingException(WorkerFailureCategories.PostconditionFailed,
                        "UpdateModuleDescription() returned false: TIA Portal did not update the module description. Check CanUpdate with network_read get_module_description_update.");
                result.Values.Add(new HardwareFeatureValueInfo { Name = "CanUpdateAfter", Value = RuntimeServiceValues.ReadProperty(updater, "CanUpdate", context.Recoverable) });
                result.ReadBack = updated is true ? "matched" : "notAvailable";
                return result;
            }
            case HardwareFeatureMethods.ExportTelecontrolDataPoints:
            {
                var management = Feature(context, HardwareFeatureNames.TelecontrolManagement, result);
                var export = Method(management, "ExportDataPoints", new[] { typeof(FileInfo) }, "TelecontrolManagement.ExportDataPoints(FileInfo)");
                var path = request.FilePath ?? throw Validation("filePath is required.");
                Files(() => PlcAlarmFiles.PrecheckExport(path, request.Overwrite == true, null));
                result.Messages.AddRange(PlcAlarmFiles.Notes(PlcAlarmFiles.Inspect(path)));
                var temporary = PlcAlarmFiles.TemporaryPath(path);
                try
                {
                    Call(export, management, new FileInfo(temporary));
                    if (!File.Exists(temporary))
                        throw new RuntimeBindingException(WorkerFailureCategories.PostconditionFailed, "ExportDataPoints returned without writing a file. Nothing was placed at the target.");
                    var (_, bytes, sha256) = Files(() => PlcAlarmFiles.Commit(temporary, path, request.Overwrite == true));
                    result.FilePath = path;
                    result.FileBytes = bytes;
                    result.FileSha256 = sha256;
                }
                finally
                {
                    PlcAlarmFiles.Discard(temporary);
                }

                result.Calls.Add("TelecontrolManagement.ExportDataPoints(FileInfo)");
                result.ReadBack = "matched";
                return result;
            }
            case HardwareFeatureMethods.ImportTelecontrolDataPoints:
            {
                var management = Feature(context, HardwareFeatureNames.TelecontrolManagement, result);
                var import = Method(management, "ImportDataPoints", new[] { typeof(FileInfo) }, "TelecontrolManagement.ImportDataPoints(FileInfo)");
                var path = request.FilePath;
                if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) || !File.Exists(path))
                    throw new RuntimeBindingException(WorkerFailureCategories.TargetNotFound, "filePath must name an existing file by its absolute path. Nothing was changed.");
                var before = Elements(TryGet(context, management, "TelecontrolDataPoints")).Count;
                // A private copy, so the file read is the file hashed (the other imports of this server do the same).
                var copy = Path.Combine(Path.GetTempPath(), "tia-mcp-telecontrol-" + Guid.NewGuid().ToString("N") + Path.GetExtension(path));
                File.Copy(path!, copy);
                try
                {
                    var bytes = File.ReadAllBytes(copy);
                    result.FilePath = path;
                    result.FileBytes = bytes.LongLength;
                    using (var sha = System.Security.Cryptography.SHA256.Create())
                        result.FileSha256 = string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
                    Call(import, management, new FileInfo(copy));
                }
                finally
                {
                    try
                    {
                        File.Delete(copy);
                    }
                    catch (IOException)
                    {
                    }
                }

                result.Calls.Add("TelecontrolManagement.ImportDataPoints(FileInfo)");
                var after = Elements(TryGet(context, management, "TelecontrolDataPoints")).Count;
                result.Values.Add(Value("dataPointsBefore", before.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                result.Values.Add(Value("dataPointsAfter", after.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                result.Messages.Add("TIA Portal decides what the import changes; the data point counts before and after are reported, the rows are not compared.");
                return result;
            }
            case HardwareFeatureMethods.ListCommunicationConnections:
            case HardwareFeatureMethods.CreateCommunicationConnection:
            case HardwareFeatureMethods.DeleteCommunicationConnection:
            {
                var management = Feature(context, HardwareFeatureNames.CommunicationManagement, result);
                var connections = Get(management, "Connections") ?? throw new RuntimeBindingException(WorkerFailureCategories.WorkerOperationFailed, "CommunicationManagement.Connections returned null.");
                if (method == HardwareFeatureMethods.CreateCommunicationConnection)
                {
                    var type = request.ConnectionType ?? throw Validation("create_communication_connection needs connectionType.");
                    var partner = context.Partner ?? throw Validation("create_communication_connection needs partner (the partner target's device item).");
                    var localNode = Node(context, request, result);
                    var partnerNode = PartnerNode(context, partner, request.PartnerNodeName, result);
                    var generic = connections.GetType().GetMethods(PublicInstance).Where(m => m.Name == "Create" && m.IsGenericMethodDefinition && m.GetParameters().Length == 3).ToList();
                    if (generic.Count != 1)
                        throw new RuntimeBindingException(WorkerFailureCategories.ApiMemberMissing, $"ConnectionComposition exposes {generic.Count} generic Create(Node, DeviceItem, Node) methods; none is chosen. Nothing was changed.");
                    var connectionType = generic[0].GetGenericArguments()[0].GetGenericParameterConstraints().Select(c => c.Assembly)
                        .Append(connections.GetType().Assembly).Distinct()
                        .Select(assembly => assembly.GetType($"{HardwareFeatureNames.ConnectionNamespace}.{type}", throwOnError: false))
                        .FirstOrDefault(t => t is not null)
                        ?? throw new RuntimeBindingException(WorkerFailureCategories.ApiMemberMissing, $"This TIA Portal installation has no connection type {HardwareFeatureNames.ConnectionNamespace}.{type}. Nothing was changed.");
                    var before = Elements(connections).Count;
                    var created = Call(generic[0].MakeGenericMethod(connectionType), connections, localNode, partner, partnerNode);
                    result.Calls.Add($"ConnectionComposition.Create<{type}>(Node, DeviceItem, Node)");
                    var after = Elements(connections).Count;
                    if (created is null || after != before + 1)
                        throw new RuntimeBindingException(WorkerFailureCategories.PostconditionFailed,
                            $"After Create<{type}> the composition holds {after} connection(s) (before: {before}). Inspect it with network_read list_communication_connections.");
                    result.Items.Add(DescribeConnection(context, created));
                    result.ReadBack = "matched";
                    return result;
                }

                var all = Elements(connections);
                if (method == HardwareFeatureMethods.DeleteCommunicationConnection)
                {
                    var index = request.ConnectionIndex ?? throw Validation("delete_communication_connection needs connectionIndex.");
                    if (index < 0 || index >= all.Count) throw new RuntimeBindingException(WorkerFailureCategories.TargetNotFound, $"There is no connection {index} (the composition holds {all.Count}). Nothing was changed.");
                    var connection = all[index]!;
                    var described = DescribeConnection(context, connection);
                    if (request.ConnectionName is { } expected && Name(connection) != expected)
                        throw new RuntimeBindingException(WorkerFailureCategories.StateChanged, $"Connection {index} is '{Name(connection)}', not '{expected}'. Nothing was changed.");
                    Call(Method(connection, "Delete", Type.EmptyTypes, "Connection.Delete()"), connection);
                    result.Calls.Add("Connection.Delete()");
                    var remaining = Elements(connections);
                    if (remaining.Count != all.Count - 1 || remaining.Any(c => Equals(c, connection)))
                        throw new RuntimeBindingException(WorkerFailureCategories.PostconditionFailed, $"The connection {described} is still listed after Delete(). Inspect it with network_read list_communication_connections.");
                    result.Items.Add(described);
                    result.ReadBack = "matched";
                    return result;
                }

                foreach (var connection in all.Take(HardwareFeatureNames.MaxConnections)) result.Items.Add(DescribeConnection(context, connection!));
                if (all.Count > HardwareFeatureNames.MaxConnections) result.Messages.Add($"Only the first {HardwareFeatureNames.MaxConnections} of {all.Count} connections are listed.");
                return result;
            }
            default:
                throw Validation($"Unsupported hardware feature '{method}'.");
        }
    }

    private static string DescribeConnection(HardwareFeatureContext context, object connection)
        => $"'{Name(connection) ?? "(unnamed)"}' type {TryGet(context, connection, "ConnectionType")?.ToString() ?? "?"}, valid {TryGet(context, connection, "IsValid")?.ToString() ?? "?"}, "
           + $"local {context.Describe(TryGet(context, connection, "LocalTarget"))} on '{TryGet(context, connection, "LocalSubnetName")}', "
           + $"partner {context.Describe(TryGet(context, connection, "PartnerTarget"))} on '{TryGet(context, connection, "PartnerSubnetName")}'";

    private static object Feature(HardwareFeatureContext context, string fullName, HardwareFeatureResultInfo result)
    {
        result.Service = fullName;
        var (service, unavailable) = RuntimeServiceValues.Find(context.ServiceTypes(context.Target), type => context.Service(context.Target, type), fullName);
        return service ?? throw new RuntimeBindingException(WorkerFailureCategories.UnsupportedCapability,
            (unavailable ?? $"{context.Describe(context.Target)} lists no '{fullName}' among its GetServiceInfos(); select the device item that offers it.") + " Nothing was changed.");
    }

    private static List<object> Nodes(HardwareFeatureContext context, HardwareFeatureResultInfo result)
        => Elements(Get(Feature(context, HardwareFeatureNames.NetworkInterface, result), "Nodes")).Where(n => n is not null).Select(n => n!).ToList();

    private static object Node(HardwareFeatureContext context, HardwareModuleRequestInfo request, HardwareFeatureResultInfo result)
    {
        var service = result.Service;
        var nodes = Nodes(context, result);
        result.Service = string.IsNullOrEmpty(service) ? HardwareFeatureNames.NetworkInterface : service;
        var matches = request.NodeName is null ? nodes : nodes.Where(n => Name(n) == request.NodeName).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new RuntimeBindingException(WorkerFailureCategories.TargetNotFound, $"The interface has no node{(request.NodeName is null ? string.Empty : $" '{request.NodeName}'")}. Nothing was changed."),
            _ => throw new RuntimeBindingException(WorkerFailureCategories.TargetAmbiguous, $"The interface has {matches.Count} nodes ({string.Join(", ", matches.Select(Name))}); name one with nodeName. Nothing was changed."),
        };
    }

    private static object PartnerNode(HardwareFeatureContext context, object partner, string? nodeName, HardwareFeatureResultInfo result)
    {
        var (service, unavailable) = RuntimeServiceValues.Find(context.ServiceTypes(partner), type => context.Service(partner, type), HardwareFeatureNames.NetworkInterface);
        if (service is null)
            throw new RuntimeBindingException(WorkerFailureCategories.UnsupportedCapability, (unavailable ?? $"The partner {context.Describe(partner)} is no network interface.") + " Nothing was changed.");
        var nodes = Elements(Get(service, "Nodes")).Where(n => n is not null && (nodeName is null || Name(n) == nodeName)).ToList();
        return nodes.Count == 1 ? nodes[0]! : throw new RuntimeBindingException(nodes.Count == 0 ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous,
            $"The partner interface has {nodes.Count} matching node(s); name one with partnerNodeName. Nothing was changed.");
    }

    private static object? Owner(object controller)
    {
        // IoController -> NetworkInterface (service) -> device item: the documented EOM parent chain (IEngineeringInstance.Parent).
        object? current = controller;
        for (var depth = 0; depth < 3 && current is not null; depth++)
        {
            if (current.GetType().Name is "DeviceItem" || current.GetType().GetProperty("DeviceItems") is not null) return current;
            current = ParentOf(current);
        }

        return controller;
    }

    private static object? ParentOf(object value)
    {
        var property = value.GetType().GetProperty("Parent", PublicInstance)
            ?? value.GetType().GetInterfaces().Select(i => i.GetProperty("Parent")).FirstOrDefault(p => p is not null);
        try
        {
            return property?.GetValue(value, null);
        }
        catch (TargetInvocationException)
        {
            return null;
        }
    }

    private static object EnumValue(Type type, string name)
        => LibraryRuntimeBinding.EnumValue(type, name)
           ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, $"'{name}' is no member of {type.FullName} ({LibraryRuntimeBinding.MemberList(type)}). Nothing was changed.");

    private static HardwareFeatureValueInfo Value(string name, string? value) => new() { Name = name, Value = RuntimeServiceValues.Project(value) };

    private static string? Name(object? value) => value?.GetType().GetProperty("Name", PublicInstance)?.GetValue(value, null) as string;

    private static object? Get(object target, string name)
    {
        var property = LibraryRuntimeBinding.Property(target.GetType(), name, out var error)
            ?? throw new RuntimeBindingException(WorkerFailureCategories.ApiMemberMissing, $"{error} Nothing was changed.");
        try
        {
            return property.GetValue(target, null);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static object? TryGet(HardwareFeatureContext context, object target, string name)
    {
        try
        {
            return target.GetType().GetProperty(name, PublicInstance) is null ? null : Get(target, name);
        }
        catch (Exception exception) when (context.Recoverable(exception))
        {
            return null;
        }
    }

    private static MethodInfo Method(object target, string name, Type[] parameters, string documented)
        => LibraryRuntimeBinding.Find(target.GetType(), name, parameters, out var error)
           ?? throw new RuntimeBindingException(WorkerFailureCategories.ApiMemberMissing, $"{error ?? $"No {name} on '{target.GetType().FullName}'."} ({documented}) Nothing was changed.");

    private static MethodInfo MethodByShape(object target, string name, Func<ParameterInfo[], bool> accepts, string documented)
    {
        var candidates = target.GetType().GetMethods(PublicInstance).Where(m => m.Name == name && !m.IsGenericMethodDefinition && accepts(m.GetParameters()))
            .GroupBy(m => m.ToString()).Select(g => g.First()).ToList();
        return candidates.Count == 1 ? candidates[0]
            : throw new RuntimeBindingException(WorkerFailureCategories.ApiMemberMissing, $"'{target.GetType().FullName}' exposes {candidates.Count} {documented}; none is chosen. Nothing was changed.");
    }

    private static object? Call(MethodInfo method, object target, params object?[] arguments) => RuntimeServiceValues.Call(method, target, arguments);

    private static List<object?> Elements(object? collection)
        => collection is IEnumerable enumerable && collection is not string ? enumerable.Cast<object?>().ToList() : new List<object?>();

    private static RuntimeBindingException Validation(string message) => new(WorkerFailureCategories.ValidationError, message + " Nothing was changed.");

    private static T Files<T>(Func<T> run)
    {
        try
        {
            return run();
        }
        catch (PlcAlarmException exception)
        {
            throw new RuntimeBindingException(exception.Category, exception.Message);
        }
    }

    private static void Files(Action run) => Files<object?>(() =>
    {
        run();
        return null;
    });
}
