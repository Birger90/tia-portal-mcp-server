using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.TechnologyObjects;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;
using Policy = TiaMcpServer.Contracts.Plc.TechnologyObjects.TechnologyObjectPolicy;

namespace TiaMcpServer.FakeWorker.Plc;

/// <summary>
/// Stateful <c>technology-objects</c> fixture. PLC_1 (Station_1) holds the technology objects <c>Axis_1</c>
/// (TO_PositioningAxis 10.0, DB 2) and <c>MeasIn_1</c> (TO_MeasuringInput 6.0) at the root and <c>Axis_2</c>
/// (TO_SpeedAxis 7.0) in <c>/Axes</c>; the empty group <c>/Axes/Line1</c> exists. Tag table <c>Axis tags</c> holds
/// <c>HwLimitMin</c>; module, modules and channel connections accept the module paths <c>[Drive_1, Module_1]</c> and
/// <c>[Drive_1, Module_2]</c>. Axes offer actor, two sensor positions and torque; MeasIn_1 offers measuringInput. Axis_1
/// also lists <c>EncoderHardwareConnectionProvider</c> without GetService returning it (live K18 shape); <c>Enc_1</c>
/// (TO_ExternalEncoder 5.0) offers one encoder-provider sensor and lists the axis provider as unavailable. Create
/// accepts only the pairs TO_PositioningAxis 10.0, TO_SpeedAxis 7.0, TO_MeasuringInput 6.0 (anything else is
/// engineering_rejected, like the Siemens skill's "does not exist or is not a valid technology object"). The parameter
/// <c>Simulation.Ignored</c> silently keeps its value (postcondition_failed); <c>Simulation.Unreadable</c> cannot be read;
/// reading <c>Simulation.Throws</c> by name fails worker_operation_failed (a planning read that is not a lookup miss).
/// Files are virtual: <c>C:\Exports\Existing.xml</c> exists, <c>C:\Imports\Axis_3.xml</c> holds TO <c>Axis_3</c>,
/// <c>C:\Imports\Nothing.xml</c> holds no object, and drive <c>N:</c> is a network drive. <c>Ident_1</c> offers the
/// ident interface; device item <c>[Reader_1, Head_1]</c> is its device. Values are converted with the real Siemens-free
/// <see cref="TechnologyObjectValues"/> rules against real CLR types (<c>Axis.Flags</c> is a <c>[Flags]</c> enum,
/// <c>Axis.Code</c> an enum with more members than are listed), and creates, connects and imports are checked with the
/// worker's Siemens-free evidence rules (<see cref="TechnologyObjectConnectionEvidence"/>). The <c>silent</c> variant
/// (scenario <c>technology-objects-silent</c>) makes Create, CreateGroup, Connect and Import return without changing
/// anything, so those checks fail. This models object access only; it is no evidence of Siemens behavior.
/// </summary>
sealed class TechnologyObjectScenario
{
    public enum FakeActorType { Analog, Drive, Stepper }

    [Flags]
    public enum FakeFlags { None = 0, Left = 1, Right = 2 }

    private sealed class FakeInterface(string name, string provider, int? index)
    {
        public string Name { get; } = name;
        public string Provider { get; } = provider;
        public int? Index { get; } = index;
        public bool Connected { get; set; }
        public Dictionary<string, (string Kind, string? Value, List<string>? Path)> Properties { get; } = new(StringComparer.Ordinal);
    }

    private sealed class FakeObject(string name, string group, string type, string version, int number)
    {
        public string Name { get; set; } = name;
        public string Group { get; set; } = group;
        public string Type { get; } = type;
        public string Version { get; } = version;
        public int Number { get; } = number;
        public List<(string Name, Type Type, object? Value)> Parameters { get; } = new();
        public List<FakeInterface> Interfaces { get; } = new();

        /// <summary>Providers GetServiceInfos() lists but GetService does not return (K18 shape).</summary>
        public List<string> UnavailableProviders { get; } = new();
        public string Content => $"<Document><SW.TechnologicalObjects.TechnologicalInstanceDB ID=\"0\"><AttributeList><Name>{Name}</Name><Revision>{Revision}</Revision></AttributeList></SW.TechnologicalObjects.TechnologicalInstanceDB></Document>";
        public int Revision { get; set; }
    }

    private const string AxisProvider = Policy.SiemensNames.AxisProvider;
    private static readonly (string Type, string Version)[] Installed = { ("TO_PositioningAxis", "10.0"), ("TO_SpeedAxis", "7.0"), ("TO_MeasuringInput", "6.0") };
    private readonly List<FakeObject> _objects = new();
    private readonly List<string> _groups = new() { "/Axes", "/Axes/Line1" };
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase)
    {
        [@"C:\Exports\Existing.xml"] = "<Document />",
        [@"C:\Imports\Axis_3.xml"] = "<Document><SW.TechnologicalObjects.TechnologicalInstanceDB ID=\"0\"><AttributeList><Name>Axis_3</Name></AttributeList></SW.TechnologicalObjects.TechnologicalInstanceDB></Document>",
        [@"C:\Imports\Nothing.xml"] = "<Document />",
    };
    private int _nextNumber = 10;

    /// <summary>An enum with more members than <see cref="Policy.MaxEnumMembers"/> lists.</summary>
    private static readonly Type ManyMembers = BuildManyMembers();

    private readonly bool silent;

    public TechnologyObjectScenario(bool silent = false)
    {
        this.silent = silent;
        var axis1 = Axis("Axis_1", "/", "TO_PositioningAxis", "10.0", 2);
        // Live K18 shape (V21 2026-11-02): the encoder provider is listed, GetService returns null; the axis provider serves.
        axis1.UnavailableProviders.Add(Policy.SiemensNames.EncoderProvider);
        _objects.Add(axis1);
        _objects.Add(Axis("Axis_2", "/Axes", "TO_SpeedAxis", "7.0", 3));
        var measuring = new FakeObject("MeasIn_1", "/", "TO_MeasuringInput", "6.0", 4);
        measuring.Parameters.Add(("Parameter.MeasuringInputType", typeof(int), 0));
        measuring.Interfaces.Add(Interface(Policy.Interfaces.MeasuringInput, Policy.SiemensNames.MeasuringInputProvider, null));
        _objects.Add(measuring);
        // The documented alternative for sensors: an encoder provider serves; the axis provider is listed but unavailable.
        var encoder = new FakeObject("Enc_1", "/", "TO_ExternalEncoder", "5.0", 6);
        encoder.UnavailableProviders.Add(AxisProvider);
        encoder.Interfaces.Add(Interface(Policy.Interfaces.Sensor, Policy.SiemensNames.EncoderProvider, null));
        _objects.Add(encoder);
        var ident = new FakeObject("Ident_1", "/", "FakeIdentType", "1.0", 5);
        ident.Interfaces.Add(Interface(Policy.Interfaces.Ident, Policy.SiemensNames.IdentProvider, null));
        _objects.Add(ident);
    }

    public string Handle(string line)
    {
        var request = JsonSerializer.Deserialize<WorkerRequest>(line, WorkerJson.Envelope)!;
        try
        {
            if (request.Method == "list_tag_tables")
                return Ok(WorkerJson.SerializePayload(new PlcTagInventoryInfo
                {
                    Plcs = { new PlcTagInventoryPlcInfo { PlcName = "PLC_1", DeviceName = "Station_1" } },
                }));
            var info = request.TechnologyObject ?? throw Invalid($"{request.Method} requires the technology object request.");
            if (info.PlcName is not null && !Same(info.PlcName, "PLC_1") && !Same(info.PlcName, "Station_1"))
                throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No PLC software named '{info.PlcName}' was found in the project.");
            return request.Method switch
            {
                TechnologyObjectMethods.List => Ok(WorkerJson.SerializePayload(List(info))),
                TechnologyObjectMethods.GetParameters => Ok(WorkerJson.SerializePayload(Parameters(info))),
                TechnologyObjectMethods.GetHardwareConnection => Ok(WorkerJson.SerializePayload(Connection(Locate(info.Name)))),
                TechnologyObjectMethods.GetContent => Ok(Locate(info.Name).Content),
                var write when TechnologyObjectMethods.IsWrite(write) => Written(write, info),
                _ => throw Invalid($"technology-objects scenario does not script '{request.Method}'."),
            };
        }
        catch (WorkerOperationException ex)
        {
            return JsonSerializer.Serialize(new WorkerResponse
            {
                Success = false, FailureCategory = ex.FailureCategory, Error = ex.Message, EngineeringError = ex.EngineeringError,
                PlcDownloadOutcome = ex.PlcDownloadOutcome,
            }, WorkerJson.Envelope);
        }
    }

    private string Written(string method, TechnologyObjectRequestInfo info)
    {
        var warnings = new List<string>();
        var result = Write(method, info, warnings);
        return JsonSerializer.Serialize(new WorkerResponse
        {
            Success = true, Payload = WorkerJson.SerializePayload(result), Warnings = warnings.Count > 0 ? warnings : null,
        }, WorkerJson.Envelope);
    }

    private TechnologyObjectInventoryInfo List(TechnologyObjectRequestInfo info)
    {
        var root = Group(info.FolderPath);
        return new TechnologyObjectInventoryInfo
        {
            Plcs =
            {
                new TechnologyObjectPlcInfo
                {
                    PlcName = "PLC_1", DeviceName = "Station_1",
                    Groups = _groups.Where(g => Within(g, root) && g != root).Select(g => new TechnologyObjectGroupInfo { Path = g, Name = g[(g.LastIndexOf('/') + 1)..] }).ToList(),
                    TechnologyObjects = _objects.Where(o => Within(o.Group, root)).Select(Describe).ToList(),
                },
            },
        };
    }

    private TechnologyObjectParametersInfo Parameters(TechnologyObjectRequestInfo info)
    {
        var to = Locate(info.Name);
        var page = new TechnologyObjectParametersInfo
        {
            PlcName = "PLC_1", DeviceName = "Station_1", Name = to.Name, GroupPath = to.Group, ParameterName = info.ParameterName, Prefix = info.Prefix,
            Offset = info.Offset ?? 0, Limit = info.Limit ?? Policy.DefaultParameterLimit,
        };
        if (info.ParameterName is not null)
        {
            if (info.ParameterName == "Simulation.Throws")
                throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, "Reading the parameter threw a recoverable Openness error.");
            var parameter = to.Parameters.FirstOrDefault(p => p.Name == info.ParameterName);
            if (parameter.Name is null) throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Technology object '{to.Name}' has no parameter '{info.ParameterName}'.");
            page.Parameters.Add(Show(parameter));
            page.MatchedCount = 1;
            return page;
        }

        var matching = to.Parameters.Where(p => info.Prefix is null || p.Name.StartsWith(info.Prefix, StringComparison.Ordinal)).ToList();
        page.TotalCount = to.Parameters.Count;
        page.MatchedCount = matching.Count;
        page.Parameters = matching.Skip(page.Offset).Take(page.Limit).Select(Show).ToList();
        page.NextOffset = page.Offset + page.Parameters.Count < matching.Count ? page.Offset + page.Parameters.Count : null;
        return page;
    }

    private TechnologyObjectWriteResultInfo Write(string method, TechnologyObjectRequestInfo info, List<string> warnings)
    {
        var result = new TechnologyObjectWriteResultInfo { Operation = method, PlcName = "PLC_1", DeviceName = "Station_1" };
        switch (method)
        {
            case TechnologyObjectMethods.Create:
            {
                var group = Group(info.FolderPath);
                // The worker's shared checks: an existing name is refused before Create, the created object is found afterwards.
                Check(TechnologyObjectWriteChecks.ExistingObject(info.Name!, _objects.FirstOrDefault(o => Same(o.Name, info.Name!))?.Group, "PLC_1"));
                if (!Installed.Contains((info.TechnologyType!, info.Version!)))
                    throw Rejected($"The technology object type '{info.TechnologyType}' in version '{info.Version}' does not exist or is not a valid technology object.");
                if (!silent)
                    _objects.Add(info.TechnologyType == "TO_MeasuringInput"
                        ? new FakeObject(info.Name!, group, info.TechnologyType!, info.Version!, _nextNumber++)
                        : Axis(info.Name!, group, info.TechnologyType!, info.Version!, _nextNumber++));
                var named = _objects.Where(o => Same(o.Name, info.Name!) && o.Group == group).ToList();
                Check(TechnologyObjectWriteChecks.Created(info.Name!, info.TechnologyType!, info.Version!, null, group, named.Count));
                var created = named[0];
                (result.Name, result.GroupPath, result.TechnologyObject) = (created.Name, group, Describe(created));
                return result;
            }

            case TechnologyObjectMethods.Delete:
            {
                var to = Locate(info.Name);
                _objects.Remove(to);
                Check(TechnologyObjectWriteChecks.Deleted(to.Name, to.Group, _objects.Any(o => Same(o.Name, to.Name) && o.Group == to.Group)));
                (result.Name, result.GroupPath) = (to.Name, to.Group);
                return result;
            }

            case TechnologyObjectMethods.Rename:
            {
                var to = Locate(info.Name);
                if (_objects.Any(o => o != to && Same(o.Name, info.NewName!))) throw Rejected($"The name '{info.NewName}' is already used.");
                to.Name = info.NewName!;
                (result.Name, result.GroupPath, result.TechnologyObject) = (to.Name, to.Group, Describe(to));
                return result;
            }

            case TechnologyObjectMethods.CreateGroup:
            {
                var path = Policy.NormalizeGroupPath(info.FolderPath);
                var parent = Group(Policy.ParentPath(path));
                Check(TechnologyObjectWriteChecks.ExistingGroup(path, _groups.Any(g => Same(g, path)), "PLC_1"));
                var before = Children(parent);
                if (!silent) _groups.Add(path);
                Check(TechnologyObjectWriteChecks.CreatedGroup(parent, path[(path.LastIndexOf('/') + 1)..], _groups.Count(g => Same(g, path)), before, Children(parent)));
                result.GroupPath = path;
                return result;
            }

            case TechnologyObjectMethods.DeleteGroup:
            {
                var path = UserGroup(info.FolderPath);
                _groups.RemoveAll(g => Within(g, path));
                _objects.RemoveAll(o => Within(o.Group, path));
                Check(TechnologyObjectWriteChecks.DeletedGroup(path, Policy.ParentPath(path), _groups.Any(g => Same(g, path))));
                result.GroupPath = path;
                return result;
            }

            case TechnologyObjectMethods.RenameGroup:
            {
                var path = UserGroup(info.FolderPath);
                var renamed = Policy.Combine(Policy.ParentPath(path), info.NewName!);
                string Move(string p) => Same(p, path) ? renamed : renamed + p[path.Length..];
                for (var i = 0; i < _groups.Count; i++) if (Within(_groups[i], path)) _groups[i] = Move(_groups[i]);
                foreach (var o in _objects.Where(o => Within(o.Group, path))) o.Group = Move(o.Group);
                result.GroupPath = renamed;
                return result;
            }

            case TechnologyObjectMethods.SetParameter:
                return SetParameter(info, result);
            case TechnologyObjectMethods.ConnectHardware:
            case TechnologyObjectMethods.DisconnectHardware:
                return ConnectOrDisconnect(method, info, result, warnings);
            case TechnologyObjectMethods.Export:
            {
                var to = Locate(info.Name);
                var target = LocalFile(info.FilePath, warnings);
                if (_files.ContainsKey(target) && !info.Overwrite) throw Invalid($"'{target}' already exists; set overwrite:true to replace it. Nothing was written.");
                _files[target] = to.Content;
                var bytes = Encoding.UTF8.GetBytes(to.Content);
                (result.Name, result.GroupPath, result.FilePath, result.FileBytes) = (to.Name, to.Group, target, bytes.LongLength);
                result.FileSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                return result;
            }

            case TechnologyObjectMethods.Import:
            {
                var source = LocalFile(info.FilePath, warnings);
                if (!_files.TryGetValue(source, out var document))
                    throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"'{source}' does not exist. Nothing was imported.");
                var group = Group(info.FolderPath);
                var before = _objects.Where(o => o.Group == group).Select(o => o.Name).ToList();
                var name = System.Xml.Linq.XDocument.Parse(document).Descendants("Name").FirstOrDefault()?.Value;
                if (name is not null && !silent)
                {
                    var existing = _objects.FirstOrDefault(o => Same(o.Name, name));
                    if (existing is not null && !info.Overwrite) throw Rejected($"An object named '{name}' already exists.");
                    if (existing is not null) _objects.Remove(existing);
                    _objects.Add(Axis(name, group, "TO_PositioningAxis", "10.0", _nextNumber++));
                }

                // The worker's shared check with a non-list (null) Import return: the new names are the only evidence.
                result.GroupPath = group;
                var (imported, failure) = TechnologyObjectWriteChecks.Imported(null, before, _objects.Where(o => o.Group == group).Select(o => o.Name).ToList(),
                    n => _objects.Any(o => Same(o.Name, n) && o.Group == group), info.Overwrite, source, group, null, warnings);
                Check(failure);
                result.ImportedObjects = imported;
                return result;
            }

            case TechnologyObjectMethods.UpdateContent:
            {
                var to = Locate(info.Name);
                var exported = to.Content;
                if (!ContentHashRules.TryParse(info.ExpectedContentHash, out _, out _) || ContentHashRules.Compute("xml", exported) != info.ExpectedContentHash)
                    throw new WorkerOperationException(WorkerFailureCategories.StateChanged, "The technology object changed since it was read.");
                if (Policy.CheckImportDocument(exported, info.Content, to.Name) is { } refusal) throw Invalid("The import was refused before anything changed: " + refusal);
                to.Revision++;
                (result.Name, result.GroupPath, result.ImportedObjects, result.ImportReturnedType, result.TechnologyObject)
                    = (to.Name, to.Group, new List<string> { to.Name }, "Fake.TechnologicalInstanceDBList", Describe(to));
                return result;
            }

            default:
                throw Invalid($"Unsupported technology object write '{method}'.");
        }
    }

    private TechnologyObjectWriteResultInfo SetParameter(TechnologyObjectRequestInfo info, TechnologyObjectWriteResultInfo result)
    {
        var to = Locate(info.Name);
        var index = to.Parameters.FindIndex(p => p.Name == info.ParameterName);
        if (index < 0) throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Technology object '{to.Name}' has no parameter '{info.ParameterName}'.");
        var (name, type, current) = to.Parameters[index];
        if (name is "Simulation.Unreadable" or "Simulation.Throws") throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, "The current value is unreadable.");
        object? requested;
        if (info.TagName is not null)
        {
            if (type != typeof(FakeTag)) throw Invalid($"Parameter '{name}' holds a {type.FullName}, not a PlcTag.");
            if (!Same(info.TableName!, "Axis tags") || info.TagName != "HwLimitMin")
                throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Tag '{info.TagName}' was not found in tag table '{info.TableName}'.");
            requested = new FakeTag(info.TagName);
        }
        else
        {
            if (type == typeof(FakeTag)) throw Invalid($"Parameter '{name}' holds a PlcTag or no value; assign a tag with tableName and tagName.");
            if (!TechnologyObjectValues.TryConvert(info.Value, type, out requested, out var error)) throw Invalid(TechnologyObjectValues.ParameterRefusal(name, to.Name, error));
        }

        result.PreviousParameter = Show(to.Parameters[index]);
        if (name != "Simulation.Ignored") to.Parameters[index] = (name, type, requested);
        result.Parameter = Show(to.Parameters[index]);
        (result.Name, result.GroupPath) = (to.Name, to.Group);
        if (name == "Simulation.Ignored")
            throw Evidence(result, $"Parameter '{name}' reads back '{result.Parameter.Value}' after writing '{info.Value}'.");
        return result;
    }

    private TechnologyObjectWriteResultInfo ConnectOrDisconnect(string method, TechnologyObjectRequestInfo info, TechnologyObjectWriteResultInfo result, List<string> warnings)
    {
        var to = Locate(info.Name);
        // The worker's rule: only the providers the interface needs, the first that serves it (K18).
        var preferred = Policy.PreferredEndpoints(to.Interfaces.Select(i => (i.Name, i.Provider, i.Index, i)), info.ConnectionInterface!);
        if (preferred.Count == 0
            && Policy.UnavailableProviderMessage(info.ConnectionInterface!, to.UnavailableProviders.Select(ListedServices.UnavailableReason)) is { } unavailable)
            throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability, unavailable);
        var (endpoint, category, message) = Policy.SelectEndpoint(preferred, info.ConnectionInterface!, info.InterfaceIndex);
        if (endpoint is null) throw new WorkerOperationException(category!, message!);
        var before = Show(endpoint);
        if (method == TechnologyObjectMethods.DisconnectHardware)
        {
            endpoint.Connected = false;
            foreach (var key in endpoint.Properties.Keys.ToList()) endpoint.Properties[key] = (TechnologyObjectValueKinds.Null, null, null);
            (result.Name, result.GroupPath, result.PreviousInterface, result.Interface) = (to.Name, to.Group, before, Show(endpoint));
            return result;
        }

        var overload = Policy.Overload(info.ConnectionInterface!, info.ConnectionKind!, info.ConnectOption is not null)
            ?? throw Invalid($"connectionKind '{info.ConnectionKind}' has no documented Connect overload on the {info.ConnectionInterface} interface.");
        var documented = $"{endpoint.Provider[(endpoint.Provider.LastIndexOf('.') + 1)..]} {endpoint.Name} Connect({overload.Signature})";
        var applied = new Dictionary<string, (string Kind, string? Value, List<string>? Path)>(StringComparer.Ordinal);
        foreach (var expectation in TechnologyObjectConnectionEvidence.Expectations(info))
            applied[expectation.Property] = expectation.Kind switch
            {
                TechnologyObjectConnectionEvidence.TargetKinds.Number => (TechnologyObjectValueKinds.Integer, expectation.Number?.ToString(System.Globalization.CultureInfo.InvariantCulture), null),
                // "Drive".Mangled models a Connect that returns but connects something else.
                TechnologyObjectConnectionEvidence.TargetKinds.Text => (TechnologyObjectValueKinds.String, expectation.Text == "\"Drive\".Mangled" ? "\"Drive\".Other" : expectation.Text, null),
                TechnologyObjectConnectionEvidence.TargetKinds.PlcTag => (TechnologyObjectValueKinds.PlcTag, Tag(info.TableName, info.TagName), null),
                TechnologyObjectConnectionEvidence.TargetKinds.EnumMember => (TechnologyObjectValueKinds.Enum, expectation.Text, null),
                TechnologyObjectConnectionEvidence.TargetKinds.Channel => (TechnologyObjectValueKinds.Channel,
                    $"{info.ChannelType ?? "Digital"} {info.ChannelIoType ?? "Input"} {info.ChannelNumber}", Module(expectation.ModulePath)),

                _ => DeviceItem(expectation.ModulePath),
            };
        if (!silent)
        {
            foreach (var (property, value) in applied) endpoint.Properties[property] = value;
            endpoint.Connected = true;
        }

        var after = Show(endpoint);
        (result.Name, result.GroupPath, result.PreviousInterface, result.Interface) = (to.Name, to.Group, before, after);
        var observations = TechnologyObjectConnectionEvidence.Observe(TechnologyObjectConnectionEvidence.Expectations(info), after);
        if (TechnologyObjectConnectionEvidence.ConnectFailure(endpoint.Name, documented, to.Name, TechnologyObjectConnectionEvidence.Connected(before),
                TechnologyObjectConnectionEvidence.Connected(after), observations, warnings) is { } failure)
            throw Evidence(result, failure);
        return result;
    }

    /// <summary>The fixture's device items: [Drive_1, Module_1], [Drive_1, Module_2] and the ident device [Reader_1, Head_1].</summary>
    private static List<string> Module(IReadOnlyList<string>? path)
    {
        var known = new[] { new[] { "Drive_1", "Module_1" }, new[] { "Drive_1", "Module_2" }, new[] { "Reader_1", "Head_1" } };
        return path is not null && known.Any(k => k.SequenceEqual(path)) ? path.ToList()
            : throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"modulePath: no device item '{string.Join("/", path ?? Array.Empty<string>())}'.");
    }

    private static (string Kind, string? Value, List<string>? Path) DeviceItem(IReadOnlyList<string>? path)
    {
        var resolved = Module(path);
        return (TechnologyObjectValueKinds.DeviceItem, resolved[^1], resolved);
    }

    private static string Tag(string? table, string? tag)
        => Same(table ?? string.Empty, "Axis tags") && tag == "HwLimitMin" ? tag
            : throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Tag '{tag}' was not found in tag table '{table}'.");

    /// <summary>The worker's path rules with an identity normalization; drive N: is a network drive.</summary>
    private static string LocalFile(string? path, List<string> warnings)
    {
        var (full, refusal) = Policy.ResolveLocalFile(path, Policy.NormalizeLocalFilePath);
        if (full is null) throw Invalid(refusal!);
        warnings.AddRange(Policy.LocalFileWarnings(full, root => Same(root, @"N:\"), _ => false));
        return full;
    }

    private int Children(string parent) => _groups.Count(g => Same(Policy.ParentPath(g), parent));

    private TechnologyObjectHardwareConnectionInfo Connection(FakeObject to) => new()
    {
        PlcName = "PLC_1", DeviceName = "Station_1", Name = to.Name, GroupPath = to.Group,
        Services = to.Interfaces.Select(i => i.Provider).Concat(to.UnavailableProviders).Distinct().ToList(),
        Interfaces = to.Interfaces.Select(Show).ToList(),
        UnavailableServices = to.UnavailableProviders.Select(ListedServices.UnavailableReason).ToList(),
    };

    private static TechnologyObjectConnectionInterfaceInfo Show(FakeInterface endpoint)
    {
        var info = new TechnologyObjectConnectionInterfaceInfo
        {
            Interface = endpoint.Name, Provider = endpoint.Provider, Index = endpoint.Index, RuntimeType = "Fake." + endpoint.Name,
            IsConnected = Policy.PropertiesFor(endpoint.Name).Contains("IsConnected") ? endpoint.Connected : null,
        };
        foreach (var name in Policy.PropertiesFor(endpoint.Name))
        {
            if (name == "IsConnected")
            {
                info.Properties.Add(new TechnologyObjectConnectionPropertyInfo { Name = name, Kind = TechnologyObjectValueKinds.Boolean, Value = endpoint.Connected ? "true" : "false", RuntimeType = "System.Boolean" });
                continue;
            }

            var (kind, value, path) = endpoint.Properties.TryGetValue(name, out var stored) ? stored : (TechnologyObjectValueKinds.Null, null, null);
            info.Properties.Add(new TechnologyObjectConnectionPropertyInfo { Name = name, Kind = kind, Value = value, RuntimeType = value is null ? null : "Fake." + kind, ModulePath = path });
        }

        return info;
    }

    private static TechnologyObjectParameterInfo Show((string Name, Type Type, object? Value) parameter)
    {
        if (parameter.Name is "Simulation.Unreadable" or "Simulation.Throws") return new TechnologyObjectParameterInfo { Name = parameter.Name, ValueKind = TechnologyObjectValueKinds.Unreadable };
        if (parameter.Value is FakeTag tag)
            return new TechnologyObjectParameterInfo { Name = parameter.Name, ValueKind = TechnologyObjectValueKinds.PlcTag, Value = tag.Name, ValueType = "Siemens.Engineering.SW.Tags.PlcTag" };
        var (kind, text) = TechnologyObjectValues.Describe(parameter.Value);
        return new TechnologyObjectParameterInfo
        {
            Name = parameter.Name, ValueKind = kind, Value = text, ValueType = parameter.Value?.GetType().FullName,
            EnumMembers = TechnologyObjectValues.EnumMembers(parameter.Value?.GetType()),
            EnumMembersTruncated = TechnologyObjectValues.EnumMembersTruncated(parameter.Value?.GetType()),
            EnumFlags = TechnologyObjectValues.IsFlags(parameter.Value?.GetType()),
        };
    }

    private static TechnologyObjectInfo Describe(FakeObject to) => new()
    {
        Name = to.Name, GroupPath = to.Group, Number = to.Number, SystemLibElement = to.Type, SystemLibVersion = to.Version,
        RuntimeType = "Siemens.Engineering.SW.TechnologicalObjects.TechnologicalInstanceDB",
    };

    private static FakeObject Axis(string name, string group, string type, string version, int number)
    {
        var axis = new FakeObject(name, group, type, version, number);
        axis.Parameters.Add(("Actor.InverseDirection", typeof(bool), false));
        axis.Parameters.Add(("Actor.Type", typeof(FakeActorType), FakeActorType.Drive));
        axis.Parameters.Add(("Axis.Code", ManyMembers, Enum.ToObject(ManyMembers, 0)));
        axis.Parameters.Add(("Axis.Flags", typeof(FakeFlags), FakeFlags.None));
        axis.Parameters.Add(("Axis.Precise", typeof(decimal), 1.5m));
        axis.Parameters.Add(("Dynamics.Velocity", typeof(double), 100.0));
        axis.Parameters.Add(("Dynamics.Acceleration", typeof(double), 500.0));
        axis.Parameters.Add(("Mechanics.LeadScrew", typeof(float), 10.0f));
        axis.Parameters.Add(("PositionLimits_HW.Active", typeof(bool), false));
        axis.Parameters.Add(("_PositionLimits_HW.MinSwitchAddress", typeof(FakeTag), null));
        axis.Parameters.Add(("Simulation.Ignored", typeof(double), 1.0));
        axis.Parameters.Add(("Simulation.Unreadable", typeof(double), 0.0));
        axis.Parameters.Add(("Simulation.Throws", typeof(double), 0.0));
        axis.Parameters.Add(("Units.LengthUnit", typeof(ushort), (ushort)1013));
        axis.Interfaces.Add(Interface(Policy.Interfaces.Actor, AxisProvider, null));
        axis.Interfaces.Add(Interface(Policy.Interfaces.Sensor, AxisProvider, 0));
        axis.Interfaces.Add(Interface(Policy.Interfaces.Sensor, AxisProvider, 1));
        axis.Interfaces.Add(Interface(Policy.Interfaces.Torque, AxisProvider, null));
        return axis;
    }

    private static FakeInterface Interface(string name, string provider, int? index) => new(name, provider, index);

    private FakeObject Locate(string? name)
    {
        var matches = _objects.Where(o => Same(o.Name, name ?? string.Empty)).ToList();
        return matches.Count == 1 ? matches[0]
            : throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No technology object named '{name}' was found in PLC 'PLC_1'.");
    }

    private string Group(string? path)
    {
        var normalized = Policy.NormalizeGroupPath(path);
        return normalized == "/" || _groups.Any(g => Same(g, normalized)) ? _groups.FirstOrDefault(g => Same(g, normalized)) ?? "/"
            : throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Technology object group '{normalized}' was not found in PLC 'PLC_1'.");
    }

    private string UserGroup(string? path)
    {
        var group = Group(path);
        return group != "/" ? group : throw Invalid("folderPath must name a user group.");
    }

    private static bool Within(string candidate, string group)
        => group == "/" || Same(candidate, group) || candidate.StartsWith(group + "/", StringComparison.OrdinalIgnoreCase);

    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static WorkerOperationException Invalid(string message) => new(WorkerFailureCategories.ValidationError, message);

    private static WorkerOperationException Rejected(string message) => new(WorkerFailureCategories.EngineeringRejected, message);

    private static void Check(TechnologyObjectWriteChecks.Failure? failure)
    {
        if (failure is not null) throw new WorkerOperationException(failure.Category, failure.Message);
    }

    /// <summary>Like the worker: a postcondition failure carries the write's before/after state as structured evidence.</summary>
    private static WorkerOperationException Evidence(TechnologyObjectWriteResultInfo result, string message)
        => new(WorkerFailureCategories.PostconditionFailed, message, plcDownloadOutcome: WorkerJson.SerializePayload(result));

    private static string Ok(string payload) => JsonSerializer.Serialize(new WorkerResponse { Success = true, Payload = payload }, WorkerJson.Envelope);

    private sealed record FakeTag(string Name);

    private static Type BuildManyMembers()
    {
        var module = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(new System.Reflection.AssemblyName("FakeTechnologyEnums"),
            System.Reflection.Emit.AssemblyBuilderAccess.Run).DefineDynamicModule("FakeTechnologyEnums");
        var builder = module.DefineEnum("Fake.ManyMembers", System.Reflection.TypeAttributes.Public, typeof(int));
        for (var i = 0; i < Policy.MaxEnumMembers + 5; i++) builder.DefineLiteral("Code" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture), i);
        return builder.CreateType();
    }
}
