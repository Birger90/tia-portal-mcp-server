using System.Collections;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.SW.Tags;
using TiaMcpServer.Contracts.Plc.TechnologyObjects;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Libraries;
using TiaMcpServer.OpennessWorker.Openness.Project;
using TiaMcpServer.OpennessWorker.Worker;
using static TiaMcpServer.Contracts.Plc.TechnologyObjects.TechnologyObjectPolicy;

namespace TiaMcpServer.OpennessWorker.Openness.TechnologyObjects;

/// <summary>
/// Hardware connections of a technology object through the documented V21 providers. The Siemens skill obtains
/// <c>AxisHardwareConnectionProvider</c> as a service of the object; every provider is therefore looked up among the
/// services <c>GetServiceInfos()</c> lists, by exact type name, and retrieved with <c>GetService(Type)</c>. Interface
/// properties are read by their documented names, <c>Connect</c> overloads (<see cref="TechnologyObjectPolicy.Overload"/>)
/// are bound by their documented parameter types. The interface is read before and after every call: a connect must read
/// as connected afterwards and read back the requested target in the overload's documented property
/// (<see cref="TechnologyObjectConnectionEvidence"/>; the Siemens skill: "a successful call is not proof of a real connection"),
/// a disconnect must read <c>IsConnected=false</c>.
/// </summary>
internal static class TechnologyObjectHardware
{
    private sealed record Endpoint(string Interface, string Provider, int? Index, object Target);

    public static TechnologyObjectHardwareConnectionInfo Read(ProjectBase project, TechnologyObjectRequestInfo info)
    {
        var located = TechnologyObjectService.Locate(project, info.PlcName, info.Name);
        var offered = TechnologyObjectRuntime.ServiceTypes(located.Object);
        var result = new TechnologyObjectHardwareConnectionInfo
        {
            PlcName = located.Plc.Software.Name ?? string.Empty, DeviceName = located.Plc.DeviceName ?? string.Empty,
            Name = located.Name, GroupPath = located.GroupPath,
            Services = offered.Select(t => t.FullName ?? t.Name).Distinct(StringComparer.Ordinal).Take(MaxServices).ToList(),
        };
        foreach (var endpoint in Endpoints(located.Object, offered, result.UnavailableServices))
        {
            if (result.Interfaces.Count == MaxInterfaces)
                throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                    $"'{located.Name}' offers more than {MaxInterfaces} connection interfaces; they are not listed partially.");
            result.Interfaces.Add(Describe(endpoint));
        }

        return result;
    }

    public static TechnologyObjectWriteResultInfo Connect(ProjectBase project, TechnologyObjectRequestInfo info, List<string> warnings)
    {
        var located = TechnologyObjectService.Locate(project, info.PlcName, info.Name);
        var connectionInterface = TechnologyObjectService.Required(info.ConnectionInterface, "connectionInterface");
        var kind = TechnologyObjectService.Required(info.ConnectionKind, "connectionKind");
        var overload = Overload(connectionInterface, kind, info.ConnectOption is not null)
            ?? throw TechnologyObjectService.Invalid($"connectionKind '{kind}' has no documented Connect overload on the {connectionInterface} interface "
                + $"(documented: {string.Join(", ", KindsFor(connectionInterface))}). Nothing was changed.");
        var endpoint = Select(located.Object, connectionInterface, info.InterfaceIndex);
        var documented = $"{endpoint.Provider.Substring(endpoint.Provider.LastIndexOf('.') + 1)} {endpoint.Interface} Connect({overload.Signature})";
        var (types, arguments, live) = Arguments(project, located, endpoint, overload.Arguments, info);
        var before = Describe(endpoint);
        LibraryReflection.Invoke(endpoint.Target, "Connect", types, arguments, documented);

        var reselected = Select(located.Object, connectionInterface, info.InterfaceIndex);
        var after = Describe(reselected);
        var result = TechnologyObjectService.Result(located.Plc, located.Name, located.GroupPath);
        result.PreviousInterface = before;
        result.Interface = after;
        var observations = TechnologyObjectConnectionEvidence.Expectations(info).Select(e => Observe(reselected.Target, e, live)).ToList();
        if (TechnologyObjectConnectionEvidence.ConnectFailure(connectionInterface, documented, located.Name, TechnologyObjectConnectionEvidence.Connected(before),
                TechnologyObjectConnectionEvidence.Connected(after), observations, warnings) is { } failure)
            throw TechnologyObjectService.Evidence(result, TechnologyObjectMethods.ConnectHardware, failure);
        return result;
    }

    public static TechnologyObjectWriteResultInfo Disconnect(ProjectBase project, TechnologyObjectRequestInfo info)
    {
        var located = TechnologyObjectService.Locate(project, info.PlcName, info.Name);
        var connectionInterface = TechnologyObjectService.Required(info.ConnectionInterface, "connectionInterface");
        if (!HasDisconnect(connectionInterface))
            throw TechnologyObjectService.Invalid($"The {connectionInterface} interface has no documented Disconnect. Nothing was changed.");
        var endpoint = Select(located.Object, connectionInterface, info.InterfaceIndex);
        var before = Describe(endpoint);
        TechnologyObjectRuntime.InvokeParameterless(endpoint.Target, "Disconnect", $"{endpoint.Provider} {connectionInterface} Disconnect()");
        var after = Describe(Select(located.Object, connectionInterface, info.InterfaceIndex));
        var result = TechnologyObjectService.Result(located.Plc, located.Name, located.GroupPath);
        result.PreviousInterface = before;
        result.Interface = after;
        if (after.IsConnected != false)
            throw TechnologyObjectService.Evidence(result, TechnologyObjectMethods.DisconnectHardware,
                $"Disconnect() returned, but the {connectionInterface} interface of '{located.Name}' reads back IsConnected="
                + $"{(after.IsConnected is null ? "(unreadable)" : "true")}; the project may have been changed.");
        return result;
    }

    /// <summary>
    /// One expected property of the live interface after a connect: object-valued targets (device items, channel, tag) are
    /// compared with the resolved argument by <see cref="object.Equals(object)"/>, addresses and indexes as integers, a DB
    /// member path as an ordinal string; an unreadable property is no match either way (Match null).
    /// </summary>
    private static TechnologyObjectConnectionEvidence.Observation Observe(object target, TechnologyObjectConnectionEvidence.Expectation expectation,
        IReadOnlyDictionary<string, object> live)
    {
        var (found, value) = TechnologyObjectRuntime.Read(target, expectation.Property);
        if (!found) return new(expectation, null, expectation.Property + " unreadable");
        var (kind, text, modulePath) = TechnologyObjectRuntime.Describe(value);
        var shown = expectation.Property + "=" + TechnologyObjectConnectionEvidence.ShownValue(kind, text, modulePath);
        var match = expectation.Kind switch
        {
            TechnologyObjectConnectionEvidence.TargetKinds.Number => Integral(value) is { } number && number == expectation.Number,
            TechnologyObjectConnectionEvidence.TargetKinds.Text => value is string path && string.Equals(path, expectation.Text, StringComparison.Ordinal),
            // Device items, channel, tag and the ConnectOption enum value: the object passed to Connect.
            _ => value is not null && expectation.Role is not null && live.TryGetValue(expectation.Role, out var argument) && Equals(value, argument),
        };
        return new(expectation, match, shown);
    }

    private static long? Integral(object? value) => value switch
    {
        sbyte or byte or short or ushort or int or uint or long => Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture),
        ulong u when u <= long.MaxValue => (long)u,
        _ => null,
    };

    /// <summary>
    /// Every interface of every documented provider the object offers, in a fixed order (the read). A provider that
    /// GetServiceInfos() lists but GetService does not return is recorded in <paramref name="unavailable"/> and skipped
    /// (D13/D14/K18, live V21 2026-11-02).
    /// </summary>
    private static IEnumerable<Endpoint> Endpoints(object technologyObject, IReadOnlyList<Type> offered, List<string> unavailable)
    {
        foreach (var name in SiemensNames.Providers)
        {
            var service = TechnologyObjectRuntime.TryService(technologyObject, offered, name, out var reason);
            if (reason is not null) unavailable.Add(reason);
            if (service is null) continue;
            foreach (var endpoint in Of(service, name)) yield return endpoint;
        }
    }

    /// <summary>
    /// The endpoints a connect or disconnect of <paramref name="connectionInterface"/> chooses among: only the providers
    /// <see cref="ProvidersFor"/> names are resolved, in its order, and the first that serves the interface wins
    /// (<see cref="PreferredEndpoints"/>). A listed provider that GetService does not return fails the write
    /// (unsupported_capability, nothing changed) only when no needed provider serves the interface (K18: a listed but
    /// unavailable EncoderHardwareConnectionProvider no longer fails an actor connect).
    /// </summary>
    private static List<Endpoint> WriteEndpoints(object technologyObject, IReadOnlyList<Type> offered, string connectionInterface)
    {
        var unavailable = new List<string>();
        foreach (var name in ProvidersFor(connectionInterface))
        {
            var service = TechnologyObjectRuntime.TryService(technologyObject, offered, name, out var reason);
            if (reason is not null) unavailable.Add(reason);
            if (service is null) continue;
            var served = Of(service, name).Where(e => e.Interface == connectionInterface).ToList();
            if (served.Count > 0) return served;
        }

        if (UnavailableProviderMessage(connectionInterface, unavailable) is { } message)
            throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability, message);
        return new List<Endpoint>();
    }

    /// <summary>The interfaces of one documented provider, by its documented members.</summary>
    private static IEnumerable<Endpoint> Of(object provider, string name)
    {
        switch (name)
        {
            case SiemensNames.AxisProvider:
                if (LibraryReflection.Required(provider, "ActorInterface", "AxisHardwareConnectionProvider.ActorInterface") is { } actor)
                    yield return new Endpoint(Interfaces.Actor, name, null, actor);
                foreach (var sensor in Sensors(provider, name)) yield return sensor;
                if (LibraryReflection.Required(provider, "TorqueInterface", "AxisHardwareConnectionProvider.TorqueInterface") is { } torque)
                    yield return new Endpoint(Interfaces.Torque, name, null, torque);
                break;
            case SiemensNames.EncoderProvider:
                foreach (var sensor in Sensors(provider, name)) yield return sensor;
                break;
            case SiemensNames.MeasuringInputProvider:
                yield return new Endpoint(Interfaces.MeasuringInput, name, null, provider);
                break;
            case SiemensNames.OutputCamProvider:
                yield return new Endpoint(Interfaces.OutputCam, name, null, provider);
                break;
            case SiemensNames.IdentProvider:
                yield return new Endpoint(Interfaces.Ident, name, null, provider);
                break;
        }
    }

    /// <summary><c>SensorInterface</c>: a composition (axis: "the sensor interfaces", indexed by position) or a single interface.</summary>
    private static IEnumerable<Endpoint> Sensors(object provider, string providerName)
    {
        var value = LibraryReflection.Required(provider, "SensorInterface", providerName.Substring(providerName.LastIndexOf('.') + 1) + ".SensorInterface");
        if (value is null) yield break;
        if (value is IEnumerable and not string)
        {
            var index = 0;
            foreach (var sensor in TechnologyObjectRuntime.Items(value, providerName + ".SensorInterface"))
                yield return new Endpoint(Interfaces.Sensor, providerName, index++, sensor);
            yield break;
        }

        yield return new Endpoint(Interfaces.Sensor, providerName, null, value);
    }

    /// <summary>Exactly one endpoint for the requested interface (and sensor position); missing or ambiguous fails before any call.</summary>
    private static Endpoint Select(object technologyObject, string connectionInterface, int? index)
    {
        if (!Interfaces.All.Contains(connectionInterface, StringComparer.Ordinal))
            throw TechnologyObjectService.Invalid($"connectionInterface must be one of {string.Join(", ", Interfaces.All)}.");
        var offered = WriteEndpoints(technologyObject, TechnologyObjectRuntime.ServiceTypes(technologyObject), connectionInterface)
            .Select(e => (e.Interface, e.Provider, e.Index, e)).ToList();
        var (endpoint, category, message) = SelectEndpoint(offered, connectionInterface, index);
        return endpoint ?? throw new WorkerOperationException(category!, message!);
    }

    /// <summary>The live arguments of the overload, in its documented order, and the resolved objects the read-back compares with.</summary>
    private static (Type[] Types, object?[] Arguments, Dictionary<string, object> Live) Arguments(
        ProjectBase project, TechnologyObjectService.Located located, Endpoint endpoint, IReadOnlyList<string> names, TechnologyObjectRequestInfo info)
    {
        var types = new List<Type>();
        var values = new List<object?>();
        var live = new Dictionary<string, object>(StringComparer.Ordinal);
        void Add(Type type, object value, string? role = null)
        {
            types.Add(type);
            values.Add(value);
            if (role is not null) live[role] = value;
        }

        foreach (var name in names)
        {
            switch (name)
            {
                case ConnectArguments.InputAddress:
                    Add(typeof(int), RequiredInt(info.InputAddress, "inputAddress"));
                    break;
                case ConnectArguments.OutputAddress:
                    Add(typeof(int), RequiredInt(info.OutputAddress, "outputAddress"));
                    break;
                case ConnectArguments.ConnectOption:
                {
                    var option = Option(endpoint.Target, info.ConnectOption ?? "Default");
                    Add(option.GetType(), option, TechnologyObjectConnectionEvidence.Roles.ConnectOption);
                    break;
                }

                case ConnectArguments.Module:
                    Add(typeof(DeviceItem), Module(project, info.ModulePath, "modulePath"), TechnologyObjectConnectionEvidence.Roles.Module);
                    break;
                case ConnectArguments.OutputModule:
                    Add(typeof(DeviceItem), Module(project, info.OutputModulePath, "outputModulePath"), TechnologyObjectConnectionEvidence.Roles.OutputModule);
                    break;
                case ConnectArguments.ChannelIndex:
                    Add(typeof(int), RequiredInt(info.ChannelIndex, "channelIndex"));
                    break;
                case ConnectArguments.Channel:
                    Add(typeof(Channel), ChannelOf(Module(project, info.ModulePath, "modulePath"), info), TechnologyObjectConnectionEvidence.Roles.Channel);
                    break;
                case ConnectArguments.Tag:
                    Add(typeof(PlcTag), TechnologyObjectService.ResolveTag(located.Plc.Software, info.TableName, info.TagName), TechnologyObjectConnectionEvidence.Roles.Tag);
                    break;
                case ConnectArguments.DbMember:
                    Add(typeof(string), TechnologyObjectService.Required(info.DbMemberPath, "dbMemberPath"));
                    break;
                default:
                    throw TechnologyObjectService.Invalid($"Unsupported Connect argument '{name}'.");
            }
        }

        return (types.ToArray(), values.ToArray(), live);
    }

    private static object Option(object anchor, string name)
        => LibraryReflection.EnumValue(TechnologyObjectRuntime.EnumType(anchor, SiemensNames.ConnectOption), name, "ConnectOption");

    /// <summary>The device item named by [device, item, sub-item, ...]: exact (ordinal) names, each level unique.</summary>
    private static DeviceItem Module(ProjectBase project, IReadOnlyList<string>? path, string field)
    {
        if (path is null || path.Count < 2 || path.Any(string.IsNullOrEmpty))
            throw TechnologyObjectService.Invalid($"{field} must list the device name and at least one device item name, for example [\"PLC_1\", \"Rack_0\", \"DI 16x24VDC_1\"].");
        var devices = ProjectDeviceEnumerator.Enumerate(project).Where(d => string.Equals(d.Name, path[0], StringComparison.Ordinal)).Take(2).ToList();
        if (devices.Count != 1)
            throw new WorkerOperationException(devices.Count == 0 ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous,
                devices.Count == 0 ? $"{field}: no device named '{path[0]}' was found." : $"{field}: several devices are named '{path[0]}'.");
        HardwareObject current = devices[0];
        for (var level = 1; level < path.Count; level++)
        {
            var matches = current.DeviceItems.Where(item => string.Equals(item.Name, path[level], StringComparison.Ordinal)).Take(2).ToList();
            var prefix = string.Join("/", path.Take(level + 1));
            if (matches.Count != 1)
                throw new WorkerOperationException(matches.Count == 0 ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous,
                    matches.Count == 0 ? $"{field}: no device item '{path[level]}' under '{string.Join("/", path.Take(level))}'." : $"{field}: '{prefix}' is ambiguous.");
            current = matches[0];
        }

        return (DeviceItem)current;
    }

    private static Channel ChannelOf(DeviceItem module, TechnologyObjectRequestInfo info)
    {
        var number = RequiredInt(info.ChannelNumber, "channelNumber");
        var matches = module.Channels.Cast<Channel>().Where(channel => channel.Number == number
                && (info.ChannelIoType is null || string.Equals(channel.IoType.ToString(), info.ChannelIoType, StringComparison.Ordinal))
                && (info.ChannelType is null || string.Equals(channel.Type.ToString(), info.ChannelType, StringComparison.Ordinal)))
            .Take(2).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                $"Device item '{module.Name}' has no channel number {number}{Filter(info)}."),
            _ => throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous,
                $"Device item '{module.Name}' has several channels number {number}{Filter(info)}; set channelIoType and/or channelType."),
        };
    }

    private static string Filter(TechnologyObjectRequestInfo info)
        => (info.ChannelIoType is null ? string.Empty : $", IoType {info.ChannelIoType}") + (info.ChannelType is null ? string.Empty : $", Type {info.ChannelType}");

    private static TechnologyObjectConnectionInterfaceInfo Describe(Endpoint endpoint)
    {
        var info = new TechnologyObjectConnectionInterfaceInfo
        {
            Interface = endpoint.Interface, Provider = endpoint.Provider, Index = endpoint.Index, RuntimeType = endpoint.Target.GetType().FullName,
        };
        foreach (var name in PropertiesFor(endpoint.Interface))
        {
            var (found, value) = TechnologyObjectRuntime.Read(endpoint.Target, name);
            if (!found)
            {
                info.Unreadable.Add(name);
                continue;
            }

            var (kind, text, modulePath) = TechnologyObjectRuntime.Describe(value);
            info.Properties.Add(new TechnologyObjectConnectionPropertyInfo
            {
                Name = name, Kind = kind, Value = text, RuntimeType = value?.GetType().FullName, ModulePath = modulePath,
            });
            if (name == "IsConnected" && value is bool connected) info.IsConnected = connected;
        }

        return info;
    }

    private static int RequiredInt(int? value, string field)
        => value ?? throw TechnologyObjectService.Invalid($"{field} is required for this connection.");
}
