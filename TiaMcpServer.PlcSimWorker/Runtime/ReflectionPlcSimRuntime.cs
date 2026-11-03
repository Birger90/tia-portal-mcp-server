using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
using TiaMcpServer.Contracts.PlcSim;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.PlcSimWorker.Runtime;

/// <summary>
/// <see cref="IPlcSimRuntime"/> over the real S7-PLCSIM Advanced API, bound by reflection through
/// <see cref="PlcSimApiSurface"/>. The API is located and loaded lazily on the first request that
/// needs it; a failed load is retried on the next request, a successful one is kept for the
/// process lifetime. Every invocation unwraps <see cref="TargetInvocationException"/> and maps
/// <c>SimulationRuntimeException</c>/<c>SimulationInitializationException</c> to
/// <c>plcsim_runtime_error</c>, and a failed type initializer to <c>plcsim_api_unavailable</c>
/// (the host then restarts this worker). Write and control return values are judged by
/// <see cref="PlcSimApiSurface.InterpretReturn"/>.
/// </summary>
internal sealed class ReflectionPlcSimRuntime : IPlcSimRuntime
{
    private readonly Func<PlcSimApiLocationInfo> _locate;
    private readonly Func<PlcSimApiLocationInfo, PlcSimLoadedApi> _load;
    private PlcSimLoadedApi? _api;
    private PlcSimApiSurface? _surface;
    private bool _initializationFailed;

    public ReflectionPlcSimRuntime(Func<PlcSimApiLocationInfo> locate, Func<PlcSimApiLocationInfo, PlcSimLoadedApi> load)
    {
        _locate = locate;
        _load = load;
    }

    /// <summary>Optional reads beyond an operation's required members, for the probe's UsedBy lists.</summary>
    private static readonly IReadOnlyDictionary<PlcSimMemberSpec, string[]> OptionalUses = new Dictionary<PlcSimMemberSpec, string[]>
    {
        [PlcSimApiCatalog.IsInitialized] = new[] { "probe_api", "list_instances" },
        [PlcSimApiCatalog.IsRuntimeManagerAvailable] = new[] { "probe_api", "list_instances" },
        [PlcSimApiCatalog.Dispose] = new[] { "every instance operation (releases the interface)" },
        [PlcSimApiCatalog.Id] = new[] { "get_instance" },
        [PlcSimApiCatalog.OperatingState] = new[] { "get_instance", "power_on", "power_off", "run", "stop", "memory_reset", "update_tag_list" },
        [PlcSimApiCatalog.OperatingMode] = new[] { "get_instance" },
        [PlcSimApiCatalog.ScaleFactor] = new[] { "get_instance" },
        [PlcSimApiCatalog.CommunicationInterface] = new[] { "get_instance" },
        [PlcSimApiCatalog.CpuType] = new[] { "get_instance" },
        [PlcSimApiCatalog.GetTagListStatus] = new[] { "list_tags" },
    };

    public PlcSimProbeResultInfo Probe()
    {
        var (api, surface) = EnsureLoaded();
        var result = new PlcSimProbeResultInfo
        {
            Location = api.Location,
            Loaded = surface is not null,
            LoadError = api.LoadError,
            AssemblyName = api.AssemblyName,
            AssemblyVersion = api.AssemblyVersion,
            FileVersion = api.FileVersion,
            ProductVersion = api.ProductVersion,
        };

        foreach (var spec in PlcSimApiCatalog.Members)
        {
            var member = surface?[spec];
            result.Members.Add(new PlcSimApiMemberInfo
            {
                Id = spec.Id,
                Kind = KindName(spec.Kind),
                Provenance = spec.Provenance,
                Candidates = spec.CandidateDisplays.ToList(),
                Resolved = member?.Resolved == true,
                ResolvedSignature = member?.Signature,
                Detail = member is null ? "the API is not loaded" : member.Detail,
                UsedBy = UsedBy(spec)
            });
        }

        foreach (var (operation, required) in PlcSimApiCatalog.Operations)
        {
            var missing = surface is null
                ? required.Select(spec => spec.Id).ToList()
                : required.Where(spec => !surface[spec].Resolved).Select(spec => spec.Id).ToList();
            result.Operations.Add(new PlcSimOperationAvailabilityInfo
            {
                Operation = operation,
                Available = operation == "probe_api" || (surface is not null && missing.Count == 0),
                MissingMembers = missing
            });
        }

        if (surface is null)
        {
            // A catalog binding that failed in a type initializer cannot be retried in this process.
            result.ApiInitializationFailed = RestartRequired;
            return result;
        }

        var stateErrors = new List<string>();
        var initializationFailed = false;
        result.IsInitialized = TryReadManagerFlag(surface, PlcSimApiCatalog.IsInitialized, stateErrors, ref initializationFailed);
        result.IsRuntimeManagerAvailable = TryReadManagerFlag(surface, PlcSimApiCatalog.IsRuntimeManagerAvailable, stateErrors, ref initializationFailed);
        result.ManagerStateError = stateErrors.Count == 0 ? null : string.Join(" ", stateErrors);
        result.ApiInitializationFailed = initializationFailed || RestartRequired;

        var instanceType = surface.InstanceType;
        result.Types.Add(DescribeType("SimulationRuntimeManager", "declared name", surface.ManagerType, members: false));
        result.Types.Add(DescribeType("IInstance", "declared name", instanceType, members: false));
        result.Types.Add(DescribeType("SInstanceInfo", "element type of SimulationRuntimeManager.RegisteredInstanceInfo", surface.InstanceInfoType, members: true));
        result.Types.Add(DescribeType("STagInfo", "element type of IInstance.TagInfos", surface.TagInfoType, members: true));
        foreach (var spec in new[] { PlcSimApiCatalog.OperatingState, PlcSimApiCatalog.OperatingMode, PlcSimApiCatalog.CommunicationInterface, PlcSimApiCatalog.CpuType })
            result.Types.Add(DescribeType($"type of {spec.Id}", "property type", surface[spec].Property?.PropertyType, members: true));
        result.Types.Add(DescribeType("ERuntimeErrorCode", "declared name", surface.ErrorCodeType, members: false));
        result.Types.Add(DescribeType("ETagListDetails", "declared GetTagListStatus parameter type (secondary hint)",
            surface.Lookup(PlcSimApiCatalog.Namespace + ".ETagListDetails"), members: false));
        result.Types.Add(DescribeType("SimulationRuntimeException", "declared name", surface.RuntimeExceptionType, members: true));
        result.Types.Add(DescribeType("SimulationInitializationException", "declared name", surface.InitializationExceptionType, members: true));

        var errorCode = new PlcSimErrorCodeMemberInfo
        {
            ExceptionType = $"{PlcSimApiCatalog.Namespace}.{PlcSimApiCatalog.RuntimeExceptionTypeName}",
            ExceptionTypeFound = surface.RuntimeExceptionType is not null,
        };
        if (surface.RuntimeExceptionType is { } exceptionType)
        {
            var (member, problem) = surface.ErrorCodeMember(exceptionType);
            errorCode.MemberName = member?.Name;
            errorCode.MemberKind = member is null ? null : member is FieldInfo ? "field" : "property";
            errorCode.Detail = problem;
        }
        else
        {
            errorCode.Detail = "the exception type was not found; failures are reported as worker_operation_failed";
        }

        errorCode.OkMemberFound = surface.HasOkMember;
        result.RuntimeErrorCode = errorCode;
        return result;
    }

    public bool RestartRequired => _initializationFailed || _surface?.InitializationFailed == true;

    public PlcSimInstanceListInfo ListInstances()
    {
        var surface = RequireSurface();
        var member = surface.Require(PlcSimApiCatalog.RegisteredInstanceInfo);
        var ignored = new List<string>();
        var initializationFailed = false;
        var result = new PlcSimInstanceListInfo
        {
            IsInitialized = TryReadManagerFlag(surface, PlcSimApiCatalog.IsInitialized, ignored, ref initializationFailed),
            IsRuntimeManagerAvailable = TryReadManagerFlag(surface, PlcSimApiCatalog.IsRuntimeManagerAvailable, ignored, ref initializationFailed),
        };
        var value = Get(surface, member, null);
        if (value is IEnumerable entries)
        {
            foreach (var entry in entries)
                result.Instances.Add(Project(surface, entry));
        }
        else if (value is not null)
        {
            throw new PlcSimOperationException(WorkerFailureCategories.WorkerOperationFailed,
                $"{member.Spec.Id} returned {value.GetType().FullName}, which is not a collection.");
        }

        result.Count = result.Instances.Count;
        return result;
    }

    public IPlcSimInstance Open(string exactName, ICollection<string> warnings)
    {
        var surface = RequireSurface();
        var create = surface.Require(PlcSimApiCatalog.CreateInterface);
        var name = surface.Require(PlcSimApiCatalog.Name);
        var target = Invoke(surface, create, null, new object?[] { exactName })
            ?? throw new PlcSimOperationException(WorkerFailureCategories.WorkerOperationFailed,
                $"{create.Spec.Id}(\"{exactName}\") returned null.");
        var instance = new Instance(surface, target, warnings);
        try
        {
            var actual = Get(surface, name, target) as string;
            if (!string.Equals(actual, exactName, StringComparison.Ordinal))
            {
                throw new PlcSimOperationException(WorkerFailureCategories.TargetNotFound,
                    actual is null
                        ? $"CreateInterface(\"{exactName}\") returned an interface without a name; no instance is selected."
                        : $"No PLCSIM instance is named exactly '{exactName}': CreateInterface returned the instance '{actual}'. Instance names match ordinally (case-sensitive).");
            }

            instance.Name = exactName;
            return instance;
        }
        catch
        {
            instance.Dispose();
            throw;
        }
    }

    private (PlcSimLoadedApi Api, PlcSimApiSurface? Surface) EnsureLoaded()
    {
        if (_surface is not null)
            return (_api!, _surface);
        var api = _load(_locate());
        if (api.Lookup is not null)
        {
            try
            {
                _surface = PlcSimApiSurface.Resolve(api.Lookup);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            {
                // A partly loadable API (a type or dependency that cannot be loaded while binding the
                // catalog) is reported as a load error, so probe_api still succeeds. Only a failed type
                // initializer is cached by .NET and requires a new process.
                _initializationFailed |= HasTypeInitializationFailure(exception);
                var root = exception;
                while (root is TargetInvocationException or TypeInitializationException && root.InnerException is not null)
                    root = root.InnerException;
                api = new PlcSimLoadedApi
                {
                    Location = api.Location,
                    LoadError = $"The S7-PLCSIM Advanced API was loaded, but its members could not be bound ({root.GetType().Name}: {root.Message}).",
                    AssemblyName = api.AssemblyName,
                    AssemblyVersion = api.AssemblyVersion,
                    FileVersion = api.FileVersion,
                    ProductVersion = api.ProductVersion,
                };
            }
        }

        _api = api;
        return (api, _surface);
    }

    private PlcSimApiSurface RequireSurface()
    {
        var (api, surface) = EnsureLoaded();
        return surface ?? throw new PlcSimOperationException(WorkerFailureCategories.PlcSimApiUnavailable,
            $"The S7-PLCSIM Advanced API is unavailable: {api.LoadError ?? api.Location.Problem ?? "not loaded"} "
            + $"Install S7-PLCSIM Advanced or set {PlcSimApiLocator.EnvironmentVariable} to the API directory; plcsim_read probe_api shows the resolution.");
    }

    private static List<string> UsedBy(PlcSimMemberSpec spec)
    {
        var uses = PlcSimApiCatalog.Operations.Where(operation => operation.Required.Contains(spec)).Select(operation => operation.Operation).ToList();
        if (OptionalUses.TryGetValue(spec, out var optional))
            uses.AddRange(optional.Where(use => !uses.Contains(use)).Select(use => use + " (optional)"));
        return uses;
    }

    private static string KindName(PlcSimMemberKind kind) => kind switch
    {
        PlcSimMemberKind.StaticProperty => "staticProperty",
        PlcSimMemberKind.StaticMethod => "staticMethod",
        PlcSimMemberKind.InstanceProperty => "instanceProperty",
        PlcSimMemberKind.InstanceMethod => "instanceMethod",
        _ => "dataMember"
    };

    private static PlcSimApiTypeInfo DescribeType(string id, string source, Type? type, bool members)
    {
        var info = new PlcSimApiTypeInfo { Id = id, Source = source, Found = type is not null, FullName = type?.FullName };
        if (type is null)
            return info;
        info.Kind = type.IsEnum ? "enum" : type.IsInterface ? "interface" : type.IsValueType ? "struct" : "class";
        if (type.IsEnum)
        {
            info.EnumNames = Enum.GetNames(type).ToList();
            // Names with their numeric values, so the sign convention of error codes can be checked live.
            info.EnumMembers = info.EnumNames.Select(name => new PlcSimApiEnumMemberInfo
            {
                Name = name,
                Value = Convert.ToString(Convert.ChangeType(Enum.Parse(type, name, ignoreCase: false), Enum.GetUnderlyingType(type),
                    System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture)!,
            }).ToList();
        }
        else if (members)
            info.Members = PlcSimObjectProjector.DataMembers(type).Select(member => new PlcSimApiTypeMemberInfo
            {
                Name = member.Name,
                Kind = member is FieldInfo ? "field" : "property",
                Type = PlcSimObjectProjector.TypeName(PlcSimObjectProjector.MemberType(member))
            }).ToList();
        return info;
    }

    private static bool? TryReadManagerFlag(PlcSimApiSurface surface, PlcSimMemberSpec spec, ICollection<string> errors, ref bool initializationFailed)
    {
        var member = surface[spec];
        if (!member.Resolved)
            return null;
        try
        {
            return Get(surface, member, null) is bool value ? value : null;
        }
        catch (PlcSimOperationException exception)
        {
            errors.Add($"{spec.Id} ({exception.Category}): {exception.Message}");
            initializationFailed |= exception.Category == WorkerFailureCategories.PlcSimApiUnavailable;
            return null;
        }
    }

    internal static object? Get(PlcSimApiSurface surface, PlcSimResolvedMember member, object? target)
    {
        try
        {
            return member.Property is { } property
                ? property.GetValue(target, null)
                : member.DataMember is FieldInfo field ? field.GetValue(target) : ((PropertyInfo)member.DataMember!).GetValue(target, null);
        }
        catch (Exception exception)
        {
            throw Map(surface, exception, member.Spec.Id);
        }
    }

    internal static object? Invoke(PlcSimApiSurface surface, PlcSimResolvedMember member, object? target, object?[] explicitArguments)
    {
        var method = member.Method!;
        var arguments = member.Candidate!.CallEquivalent
            ? method.GetParameters().Select(DefaultArgument).ToArray()
            : explicitArguments;
        try
        {
            return method.Invoke(target, arguments);
        }
        catch (Exception exception)
        {
            throw Map(surface, exception, member.Spec.Id);
        }
    }

    private static object? DefaultArgument(ParameterInfo parameter)
    {
        var value = parameter.DefaultValue;
        if (value is DBNull || value == Missing.Value)
            return Type.Missing;
        return value is not null && parameter.ParameterType.IsEnum && value.GetType() != parameter.ParameterType
            ? Enum.ToObject(parameter.ParameterType, value)
            : value;
    }

    /// <summary>
    /// Unwraps reflection wrappers and categorizes an API exception without parsing its message. A
    /// <see cref="TypeInitializationException"/> anywhere in the chain (for example the static
    /// <c>SimulationRuntimeManager</c> initializer failing because the Runtime Manager was not
    /// running) is <c>plcsim_api_unavailable</c>: .NET caches it for the process lifetime, and the host
    /// restarts the PLCSIM worker because the surface then reports <see cref="PlcSimApiSurface.InitializationFailed"/>.
    /// </summary>
    internal static PlcSimOperationException Map(PlcSimApiSurface surface, Exception exception, string memberId)
    {
        var typeInitialization = HasTypeInitializationFailure(exception);
        while (exception is TargetInvocationException or TypeInitializationException && exception.InnerException is not null)
            exception = exception.InnerException;

        if (exception is PlcSimOperationException categorized)
            return categorized;

        var typeName = exception.GetType().Name;
        var detail = new PlcSimErrorInfo { ExceptionType = typeName };
        var apiException = surface.IsRuntimeException(exception) || surface.IsInitializationException(exception);
        if (typeInitialization)
        {
            surface.MarkInitializationFailed();
            return new PlcSimOperationException(WorkerFailureCategories.PlcSimApiUnavailable,
                $"The S7-PLCSIM Advanced API could not initialize during {memberId} ({typeName}{ErrorCodeText(surface, exception, apiException ? detail : null)}: "
                + $"{exception.Message}). A failed type initializer stays failed for the whole worker process, so the PLCSIM worker is restarted "
                + "before the next call; make sure the S7-PLCSIM Advanced Runtime Manager is running, then retry.", detail);
        }

        if (!apiException)
            return new PlcSimOperationException(WorkerFailureCategories.WorkerOperationFailed,
                $"{memberId} failed with {typeName}: {exception.Message}", detail);

        var message = $"S7-PLCSIM Advanced raised {typeName}{ErrorCodeText(surface, exception, detail)} during {memberId}: {exception.Message}";
        // K41 (live 2026-11-02): after a download from the TIA Portal UI every tag read failed with NotUpToDate (-47) until
        // plcsim_write update_tag_list ran; the bare error code did not say so.
        if (string.Equals(detail.RuntimeErrorCode, "NotUpToDate", StringComparison.Ordinal))
            message += " The instance's tag list is out of date (for example after a download): run plcsim_write update_tag_list, then retry.";
        return new PlcSimOperationException(WorkerFailureCategories.PlcSimRuntimeError, message, detail);
    }

    /// <summary>
    /// Projects an API value; a getter failing with a (cached) type initializer still becomes an
    /// <c>&lt;unreadable:…&gt;</c> marker, but also flags the worker for a restart.
    /// </summary>
    internal static JsonElement Project(PlcSimApiSurface surface, object? value)
        => PlcSimObjectProjector.Project(value, observeReadFailure: exception =>
        {
            if (HasTypeInitializationFailure(exception))
                surface.MarkInitializationFailed();
        });

    /// <summary>Upper bound of the inner-exception walk (guards against pathological or cyclic chains).</summary>
    internal const int MaxInnerExceptionDepth = 32;

    /// <summary>True when a <see cref="TypeInitializationException"/> appears anywhere in the bounded inner-exception chain.</summary>
    internal static bool HasTypeInitializationFailure(Exception exception)
    {
        var depth = 0;
        for (Exception? current = exception; current is not null && depth < MaxInnerExceptionDepth; current = current.InnerException, depth++)
        {
            if (current is TypeInitializationException)
                return true;
        }

        return false;
    }

    /// <summary>", member = name (number)" read from the exception's single ERuntimeErrorCode member, filling <paramref name="detail"/>; empty when unavailable.</summary>
    private static string ErrorCodeText(PlcSimApiSurface surface, Exception exception, PlcSimErrorInfo? detail)
    {
        if (detail is null)
            return string.Empty;

        var codeText = string.Empty;
        var (codeMember, _) = surface.ErrorCodeMember(exception.GetType());
        if (codeMember is not null)
        {
            try
            {
                var code = codeMember is FieldInfo field ? field.GetValue(exception) : ((PropertyInfo)codeMember).GetValue(exception, null);
                if (code is not null)
                {
                    detail.RuntimeErrorCode = code.ToString();
                    detail.RuntimeErrorCodeMember = codeMember.Name;
                    var numeric = code is Enum ? Enum.Format(code.GetType(), code, "D") : null;
                    codeText = $", {codeMember.Name} = {detail.RuntimeErrorCode}{(numeric is null ? string.Empty : $" ({numeric})")}";
                }
            }
            catch (Exception readFailure) when (readFailure is TargetInvocationException or ArgumentException or InvalidOperationException)
            {
                codeText = $", {codeMember.Name} unreadable";
            }
        }

        return codeText;
    }

    private sealed class Instance : IPlcSimInstance
    {
        private readonly PlcSimApiSurface _surface;
        private readonly object _target;
        private readonly ICollection<string> _warnings;
        private bool _disposed;

        public Instance(PlcSimApiSurface surface, object target, ICollection<string> warnings)
        {
            _surface = surface;
            _target = target;
            _warnings = warnings;
        }

        public string Name { get; set; } = string.Empty;

        public PlcSimPropertyRead ReadProperty(PlcSimInstanceProperty property)
        {
            var spec = PlcSimApiCatalog.For(property);
            var member = _surface[spec];
            if (!member.Resolved)
                return PlcSimPropertyRead.Missing(spec.Id, $"{spec.Id} is not available: {member.Detail}");
            try
            {
                return PlcSimPropertyRead.Of(spec.Id, Project(_surface, Get(_surface, member, _target)));
            }
            catch (PlcSimOperationException exception)
            {
                return PlcSimPropertyRead.Missing(spec.Id, $"{spec.Id} could not be read ({exception.Category}): {exception.Message}");
            }
        }

        public PlcSimInvocation Control(PlcSimControlAction action)
        {
            var member = _surface.Require(PlcSimApiCatalog.For(action));
            var parameters = member.Candidate!.Parameters;
            int? timeout = !member.Candidate.CallEquivalent && parameters.Count == 1 && parameters[0].ClrType == typeof(uint)
                ? PlcSimApiCatalog.ControlTimeoutMs
                : null;
            var returned = Invoke(_surface, member, _target, timeout is { } ms ? new object?[] { (uint)ms } : Array.Empty<object?>());
            return Describe(member, timeout, returned);
        }

        /// <summary>
        /// Counts every entry, matches names only when filtering, and projects only the first
        /// <paramref name="max"/> matches (projection is the expensive part of a large tag list).
        /// </summary>
        public PlcSimTagInfoSnapshot ReadTagInfos(string? nameFilter, int max)
        {
            var member = _surface.Require(PlcSimApiCatalog.TagInfos);
            var nameMember = _surface[PlcSimApiCatalog.TagInfoName];
            if (nameFilter is not null && !nameMember.Resolved)
                return new PlcSimTagInfoSnapshot(Array.Empty<JsonElement>(), 0, 0, nameMember.Detail);

            var value = Get(_surface, member, _target);
            var tags = new List<JsonElement>();
            int total = 0, matched = 0;
            if (value is IEnumerable entries)
            {
                foreach (var entry in entries)
                {
                    total++;
                    if (nameFilter is not null
                        && (entry is null || (Get(_surface, nameMember, entry) as string)?.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) is not >= 0))
                        continue;
                    matched++;
                    if (tags.Count < max)
                        tags.Add(Project(_surface, entry));
                }
            }
            else if (value is not null)
            {
                throw new PlcSimOperationException(WorkerFailureCategories.WorkerOperationFailed,
                    $"{member.Spec.Id} returned {value.GetType().FullName}, which is not a collection.");
            }

            return new PlcSimTagInfoSnapshot(tags, total, matched, nameMember.Resolved ? null : nameMember.Detail);
        }

        public PlcSimPropertyRead ReadTagListStatus()
        {
            var spec = PlcSimApiCatalog.GetTagListStatus;
            var member = _surface[spec];
            if (!member.Resolved)
                return PlcSimPropertyRead.Missing(spec.Id, $"{spec.Id} is not available: {member.Detail}");
            var parameters = member.Method!.GetParameters();
            var arguments = parameters.Select(parameter => DefaultOf(parameter.ParameterType.GetElementType()!)).ToArray();
            try
            {
                Invoke(_surface, member, _target, arguments);
            }
            catch (PlcSimOperationException exception)
            {
                return PlcSimPropertyRead.Missing(spec.Id, $"{spec.Id} failed ({exception.Category}): {exception.Message}");
            }

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                for (var index = 0; index < parameters.Length; index++)
                {
                    writer.WritePropertyName(parameters[index].Name ?? $"arg{index}");
                    writer.WriteRawValue(Project(_surface, arguments[index]).GetRawText());
                }

                writer.WriteEndObject();
            }

            return PlcSimPropertyRead.Of(spec.Id, PlcSimValueCodec.Parse(Encoding.UTF8.GetString(stream.ToArray())));
        }

        public object Read(string tag, string dataType)
        {
            var member = _surface.Require(PlcSimApiCatalog.Reads[dataType]);
            var value = Invoke(_surface, member, _target, new object?[] { tag });
            if (!PlcSimValueCodec.TryFromApiValue(dataType, value, out var typed, out var error))
                throw new PlcSimOperationException(WorkerFailureCategories.WorkerOperationFailed, $"{member.Spec.Id}(\"{tag}\"): {error}.");
            return typed!;
        }

        public PlcSimInvocation Write(string tag, string dataType, object value)
        {
            var member = _surface.Require(PlcSimApiCatalog.Writes[dataType]);
            var returned = Invoke(_surface, member, _target, new[] { tag, value });
            return Describe(member, null, returned);
        }

        public void SetScaleFactor(double value)
        {
            var member = _surface.Require(PlcSimApiCatalog.ScaleFactor);
            var property = member.Property!;
            object argument;
            if (property.PropertyType == typeof(double))
                argument = value;
            else if (property.PropertyType == typeof(float))
                argument = PlcSimValueCodec.TryToSingle(value, out var single, out var rangeError)
                    ? single!
                    : throw new PlcSimOperationException(WorkerFailureCategories.ValidationError,
                        $"scaleFactor {value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} cannot be set: the loaded {member.Spec.Id} is Single and {rangeError}. Nothing was changed.");
            else
                throw PlcSimOperationException.MemberMissing(member.Spec.Id,
                    $"{member.Spec.Id} has type {PlcSimObjectProjector.TypeName(property.PropertyType)}; only Double or Single can be set.");
            try
            {
                property.SetValue(_target, argument, null);
            }
            catch (Exception exception)
            {
                throw Map(_surface, exception, member.Spec.Id);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            var member = _surface[PlcSimApiCatalog.Dispose];
            if (!member.Resolved)
            {
                _warnings.Add($"{member.Spec.Id} is not available ({member.Detail}); the instance interface was not released.");
                return;
            }

            try
            {
                Invoke(_surface, member, _target, Array.Empty<object?>());
            }
            catch (PlcSimOperationException exception)
            {
                _warnings.Add($"Releasing the instance interface failed ({exception.Category}): {exception.Message}");
            }
        }

        private PlcSimInvocation Describe(PlcSimResolvedMember member, int? timeout, object? returned)
        {
            var method = member.Method!;
            var (check, code, value) = _surface.InterpretReturn(method.ReturnType, returned, out var zeroFallback);
            return new PlcSimInvocation(
                PlcSimResolvedMember.MethodSignature(method),
                timeout,
                PlcSimObjectProjector.TypeName(method.ReturnType),
                method.ReturnType == typeof(void) ? null : Project(_surface, returned),
                check,
                code,
                value,
                zeroFallback);
        }

        private static object? DefaultOf(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
