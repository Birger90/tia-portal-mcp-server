using System.Reflection;
using TiaMcpServer.Contracts.PlcSim;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.PlcSimWorker.Runtime;

internal enum PlcSimMemberKind
{
    StaticProperty,
    StaticMethod,
    InstanceProperty,
    InstanceMethod,
    /// <summary>A public field or property of a value type found at runtime (for example the element type of TagInfos).</summary>
    DataMember
}

/// <summary>
/// Where a member expectation comes from: <see cref="ManualName"/> names and
/// <see cref="ReferenceSignature"/> signatures are verified against Siemens documentation; the rest is not.
/// </summary>
internal static class PlcSimProvenance
{
    /// <summary>The member name appears in the overview tables of the S7-PLCSIM Advanced API manual V6.0 UPD1 (A5E53246567-AB); its parameter list does not.</summary>
    public const string ManualName = "manualName";

    /// <summary>Name and parameter list both appear in that manual's overview (CreateInterface(string in_InstanceName)).</summary>
    public const string ManualSignature = "manualSignature";

    /// <summary>Not in the obtainable manual excerpt; taken from an unverified secondary source and resolved only if present.</summary>
    public const string SecondaryHint = "secondaryHint";

    /// <summary>
    /// Name and signature (return type, parameter list) appear on the IInstance page of the Siemens
    /// S7-PLCSIM Advanced developer reference for API 8.0 (C++/CLI notation). probe_api remains the
    /// check for the installed version.
    /// </summary>
    public const string ReferenceSignature = "referenceSignature";
}

/// <summary>A parameter type: a CLR type, or a type of the PLCSIM API namespace looked up by simple name.</summary>
internal sealed class PlcSimParameter
{
    private PlcSimParameter(Type? clrType, string? apiTypeName, bool byRef)
    {
        ClrType = clrType;
        ApiTypeName = apiTypeName;
        ByRef = byRef;
    }

    public Type? ClrType { get; }

    public string? ApiTypeName { get; }

    public bool ByRef { get; }

    public static PlcSimParameter Clr(Type type, bool byRef = false) => new(type, null, byRef);

    public static PlcSimParameter Api(string simpleTypeName, bool byRef = false) => new(null, simpleTypeName, byRef);

    public string Display => (ByRef ? "ref " : string.Empty) + (ClrType is not null ? PlcSimObjectProjector.TypeName(ClrType) : ApiTypeName);

    public Type? Resolve(Func<string, Type?> lookup)
        => ClrType ?? lookup(PlcSimApiCatalog.Namespace + "." + ApiTypeName);
}

/// <summary>
/// One declared signature candidate. <see cref="CallEquivalent"/> candidates mirror the C# call
/// <c>x.Name()</c>: they match a parameterless overload first and otherwise an overload whose every
/// parameter is optional, invoked with the declared defaults.
/// </summary>
internal sealed class PlcSimSignature
{
    public PlcSimSignature(string source, IReadOnlyList<PlcSimParameter> parameters, Type? returnType = null, bool callEquivalent = false)
    {
        Source = source;
        Parameters = parameters;
        ReturnType = returnType;
        CallEquivalent = callEquivalent;
    }

    public string Source { get; }

    public IReadOnlyList<PlcSimParameter> Parameters { get; }

    /// <summary>When set, the overload must return exactly this type.</summary>
    public Type? ReturnType { get; }

    public bool CallEquivalent { get; }

    public string Display(string name)
    {
        var text = CallEquivalent
            ? $"{name}() [C# call: no parameters, or only optional parameters with their defaults]"
            : $"{name}({string.Join(", ", Parameters.Select(parameter => parameter.Display))})";
        if (ReturnType is not null)
            text += " -> " + PlcSimObjectProjector.TypeName(ReturnType);
        return $"{text} [{Source}]";
    }

    public static PlcSimSignature Call(string source) => new(source, Array.Empty<PlcSimParameter>(), callEquivalent: true);
}

internal sealed class PlcSimMemberSpec
{
    public PlcSimMemberSpec(string owner, string name, PlcSimMemberKind kind, string provenance,
        IReadOnlyList<PlcSimSignature>? candidates = null, bool needsSetter = false, Type? dataMemberType = null)
    {
        Owner = owner;
        Name = name;
        Kind = kind;
        Provenance = provenance;
        Candidates = candidates ?? Array.Empty<PlcSimSignature>();
        NeedsSetter = needsSetter;
        DataMemberType = dataMemberType;
    }

    public string Owner { get; }

    public string Name { get; }

    public string Id => $"{Owner}.{Name}";

    public PlcSimMemberKind Kind { get; }

    public string Provenance { get; }

    public IReadOnlyList<PlcSimSignature> Candidates { get; }

    public bool NeedsSetter { get; }

    /// <summary>For <see cref="PlcSimMemberKind.DataMember"/>: the required member type.</summary>
    public Type? DataMemberType { get; }

    public IReadOnlyList<string> CandidateDisplays => Kind switch
    {
        PlcSimMemberKind.StaticMethod or PlcSimMemberKind.InstanceMethod => Candidates.Select(candidate => candidate.Display(Name)).ToList(),
        PlcSimMemberKind.DataMember => new[] { $"{Name} : {PlcSimObjectProjector.TypeName(DataMemberType!)} (public field or property) [{Provenance}]" },
        _ => new[] { $"{Name} {{ get;{(NeedsSetter ? " set;" : string.Empty)} }} [{Provenance}]" }
    };
}

/// <summary>
/// The single catalog of every S7-PLCSIM Advanced member the worker uses. Nothing outside this
/// catalog is ever looked up, and nothing is matched fuzzily: a member resolves only through one
/// of its declared candidates, by exact (ordinal) name and exact parameter types.
/// </summary>
internal static class PlcSimApiCatalog
{
    public const string Namespace = "Siemens.Simatic.Simulation.Runtime";
    public const string ManagerTypeName = "SimulationRuntimeManager";
    public const string InstanceTypeName = "IInstance";
    public const string RuntimeExceptionTypeName = "SimulationRuntimeException";
    public const string InitializationExceptionTypeName = "SimulationInitializationException";
    public const string ErrorCodeTypeName = "ERuntimeErrorCode";
    public const string TagInfoOwner = "STagInfo";

    /// <summary>
    /// The timeout passed when a control method resolves through its <c>(UInt32 in_TimeOut_ms)</c>
    /// overload (PowerOn, PowerOff, Run, Stop, MemoryReset; in the developer reference for API 8.0).
    /// </summary>
    public const int ControlTimeoutMs = 60_000;

    public const int PowerOnTimeoutMs = ControlTimeoutMs;

    /// <summary>
    /// The <c>ERuntimeErrorCode</c> member name that means success when present (the native code is
    /// <c>SREC_OK</c>; the .NET name is verified only at runtime). Only when the loaded enum has no
    /// such member is zero assumed to be success, with a warning (see <c>PlcSimApiSurface.InterpretReturn</c>).
    /// </summary>
    public const string ErrorCodeOkMemberName = "OK";

    /// <summary>
    /// The <c>CommunicationInterface</c> value name of the PLCSIM softbus. The native enum is
    /// <c>SRCI_SOFTBUS = 1</c>; this .NET name is unverified, and a different value is warned about.
    /// </summary>
    public const string SoftbusCommunicationInterface = "Softbus";

    private static readonly PlcSimParameter StringParameter = PlcSimParameter.Clr(typeof(string));

    public static readonly PlcSimMemberSpec IsInitialized = new(ManagerTypeName, "IsInitialized", PlcSimMemberKind.StaticProperty, PlcSimProvenance.ManualName);
    public static readonly PlcSimMemberSpec IsRuntimeManagerAvailable = new(ManagerTypeName, "IsRuntimeManagerAvailable", PlcSimMemberKind.StaticProperty, PlcSimProvenance.ManualName);
    public static readonly PlcSimMemberSpec RegisteredInstanceInfo = new(ManagerTypeName, "RegisteredInstanceInfo", PlcSimMemberKind.StaticProperty, PlcSimProvenance.ManualName);
    public static readonly PlcSimMemberSpec CreateInterface = new(ManagerTypeName, "CreateInterface", PlcSimMemberKind.StaticMethod, PlcSimProvenance.ManualSignature,
        new[] { new PlcSimSignature("manual", new[] { StringParameter }) });

    // IInstance members below marked ReferenceSignature: IInstance page of the Siemens developer reference, API 8.0.
    public static readonly PlcSimMemberSpec Dispose = new(InstanceTypeName, "Dispose", PlcSimMemberKind.InstanceMethod, PlcSimProvenance.ReferenceSignature,
        new[] { new PlcSimSignature("developer reference API 8.0: IInstance inherits System::IDisposable", Array.Empty<PlcSimParameter>()) });
    public static readonly PlcSimMemberSpec Name = new(InstanceTypeName, "Name", PlcSimMemberKind.InstanceProperty, PlcSimProvenance.ReferenceSignature);
    public static readonly PlcSimMemberSpec Id = new(InstanceTypeName, "ID", PlcSimMemberKind.InstanceProperty, PlcSimProvenance.ReferenceSignature);
    public static readonly PlcSimMemberSpec OperatingState = new(InstanceTypeName, "OperatingState", PlcSimMemberKind.InstanceProperty, PlcSimProvenance.ReferenceSignature);
    public static readonly PlcSimMemberSpec OperatingMode = new(InstanceTypeName, "OperatingMode", PlcSimMemberKind.InstanceProperty, PlcSimProvenance.ReferenceSignature);
    public static readonly PlcSimMemberSpec ScaleFactor = new(InstanceTypeName, "ScaleFactor", PlcSimMemberKind.InstanceProperty, PlcSimProvenance.ReferenceSignature, needsSetter: true);
    public static readonly PlcSimMemberSpec CommunicationInterface = new(InstanceTypeName, "CommunicationInterface", PlcSimMemberKind.InstanceProperty, PlcSimProvenance.ReferenceSignature);
    public static readonly PlcSimMemberSpec CpuType = new(InstanceTypeName, "CPUType", PlcSimMemberKind.InstanceProperty, PlcSimProvenance.ReferenceSignature);
    public static readonly PlcSimMemberSpec TagInfos = new(InstanceTypeName, "TagInfos", PlcSimMemberKind.InstanceProperty, PlcSimProvenance.ReferenceSignature);

    // The (UInt32) timeout overload comes first so a state change completes before operatingStateAfter is read;
    // the parameterless fallback may return earlier. Per the developer reference only PowerOn returns ERuntimeErrorCode;
    // the others return void.
    public static readonly PlcSimMemberSpec PowerOn = TimedControl("PowerOn");
    public static readonly PlcSimMemberSpec PowerOff = TimedControl("PowerOff");
    public static readonly PlcSimMemberSpec Run = TimedControl("Run");
    public static readonly PlcSimMemberSpec Stop = TimedControl("Stop");
    public static readonly PlcSimMemberSpec MemoryReset = TimedControl("MemoryReset");
    public static readonly PlcSimMemberSpec UpdateTagList = Control("UpdateTagList");

    public static readonly PlcSimMemberSpec GetTagListStatus = new(InstanceTypeName, "GetTagListStatus", PlcSimMemberKind.InstanceMethod, PlcSimProvenance.ReferenceSignature, new[]
    {
        new PlcSimSignature("developer reference API 8.0: void GetTagListStatus(ETagListDetails%, bool%)", new[] { PlcSimParameter.Api("ETagListDetails", byRef: true), PlcSimParameter.Clr(typeof(bool), byRef: true) })
    });

    /// <summary>The tag name member of the TagInfos element type; needed only by list_tags nameFilter.</summary>
    public static readonly PlcSimMemberSpec TagInfoName = new(TagInfoOwner, "Name", PlcSimMemberKind.DataMember, PlcSimProvenance.SecondaryHint, dataMemberType: typeof(string));

    public static readonly IReadOnlyDictionary<string, PlcSimMemberSpec> Reads = PlcSimDataTypes.All.ToDictionary(
        dataType => dataType,
        dataType => new PlcSimMemberSpec(InstanceTypeName, PlcSimDataTypes.ReadMethod(dataType), PlcSimMemberKind.InstanceMethod, PlcSimProvenance.ReferenceSignature,
            new[] { new PlcSimSignature("developer reference API 8.0", new[] { StringParameter }, PlcSimDataTypes.ClrType(dataType)) }),
        StringComparer.Ordinal);

    public static readonly IReadOnlyDictionary<string, PlcSimMemberSpec> Writes = PlcSimDataTypes.Writable.ToDictionary(
        dataType => dataType,
        dataType => new PlcSimMemberSpec(InstanceTypeName, PlcSimDataTypes.WriteMethod(dataType)!, PlcSimMemberKind.InstanceMethod, PlcSimProvenance.ReferenceSignature,
            new[] { new PlcSimSignature("developer reference API 8.0 (returns void; any return type is accepted)", new[] { StringParameter, PlcSimParameter.Clr(PlcSimDataTypes.ClrType(dataType)) }) }),
        StringComparer.Ordinal);

    public static IReadOnlyList<PlcSimMemberSpec> Members { get; } = new[]
    {
        IsInitialized, IsRuntimeManagerAvailable, RegisteredInstanceInfo, CreateInterface,
        Dispose, Name, Id, OperatingState, OperatingMode, ScaleFactor, CommunicationInterface, CpuType, TagInfos,
        PowerOn, PowerOff, Run, Stop, MemoryReset, UpdateTagList, GetTagListStatus, TagInfoName
    }.Concat(PlcSimDataTypes.All.Select(dataType => Reads[dataType]))
        .Concat(PlcSimDataTypes.Writable.Select(dataType => Writes[dataType]))
        .ToList();

    public static PlcSimMemberSpec For(PlcSimInstanceProperty property) => property switch
    {
        PlcSimInstanceProperty.Name => Name,
        PlcSimInstanceProperty.Id => Id,
        PlcSimInstanceProperty.OperatingState => OperatingState,
        PlcSimInstanceProperty.OperatingMode => OperatingMode,
        PlcSimInstanceProperty.ScaleFactor => ScaleFactor,
        PlcSimInstanceProperty.CommunicationInterface => CommunicationInterface,
        _ => CpuType
    };

    public static PlcSimMemberSpec For(PlcSimControlAction action) => action switch
    {
        PlcSimControlAction.PowerOn => PowerOn,
        PlcSimControlAction.PowerOff => PowerOff,
        PlcSimControlAction.Run => Run,
        PlcSimControlAction.Stop => Stop,
        PlcSimControlAction.MemoryReset => MemoryReset,
        _ => UpdateTagList
    };

    /// <summary>Members an operation cannot run without (optional members only degrade it).</summary>
    public static IReadOnlyList<(string Operation, IReadOnlyList<PlcSimMemberSpec> Required)> Operations { get; } = BuildOperations();

    private static PlcSimMemberSpec Control(string name)
        => new(InstanceTypeName, name, PlcSimMemberKind.InstanceMethod, PlcSimProvenance.ReferenceSignature,
            new[] { PlcSimSignature.Call("developer reference API 8.0: parameterless overload") });

    private static PlcSimMemberSpec TimedControl(string name)
        => new(InstanceTypeName, name, PlcSimMemberKind.InstanceMethod, PlcSimProvenance.ReferenceSignature, new[]
        {
            new PlcSimSignature("developer reference API 8.0: (UINT32 in_TimeOut_ms)", new[] { PlcSimParameter.Clr(typeof(uint)) }),
            PlcSimSignature.Call("developer reference API 8.0: parameterless overload")
        });

    private static IReadOnlyList<(string, IReadOnlyList<PlcSimMemberSpec>)> BuildOperations()
    {
        PlcSimMemberSpec[] Open(params PlcSimMemberSpec[] more) => new[] { CreateInterface, Name }.Concat(more).ToArray();
        var operations = new List<(string, IReadOnlyList<PlcSimMemberSpec>)>
        {
            ("probe_api", Array.Empty<PlcSimMemberSpec>()),
            ("list_instances", new[] { RegisteredInstanceInfo }),
            ("get_instance", Open()),
            ("list_tags", Open(TagInfos)),
            ("list_tags (nameFilter)", Open(TagInfos, TagInfoName)),
        };
        operations.AddRange(PlcSimDataTypes.All.Select(dataType => ($"read_tags ({dataType})", (IReadOnlyList<PlcSimMemberSpec>)Open(Reads[dataType]))));
        operations.Add(("power_on", Open(PowerOn)));
        operations.Add(("power_off", Open(PowerOff)));
        operations.Add(("run", Open(Run)));
        operations.Add(("stop", Open(Stop)));
        operations.Add(("memory_reset", Open(MemoryReset)));
        operations.Add(("update_tag_list", Open(UpdateTagList)));
        operations.AddRange(PlcSimDataTypes.Writable.Select(dataType => ($"write_tags ({dataType})", (IReadOnlyList<PlcSimMemberSpec>)Open(Writes[dataType]))));
        operations.Add(("set_scale_factor", Open(ScaleFactor)));
        operations.Add(("plcsim_test", Open()));
        return operations;
    }
}

/// <summary>A catalog member after resolution against a loaded assembly.</summary>
internal sealed class PlcSimResolvedMember
{
    public PlcSimResolvedMember(PlcSimMemberSpec spec, string? detail)
    {
        Spec = spec;
        Detail = detail;
    }

    public PlcSimMemberSpec Spec { get; }

    public bool Resolved => Property is not null || Method is not null || DataMember is not null;

    public PropertyInfo? Property { get; init; }

    public MethodInfo? Method { get; init; }

    public MemberInfo? DataMember { get; init; }

    public PlcSimSignature? Candidate { get; init; }

    public string? Detail { get; }

    public string? Signature => Method is not null
        ? MethodSignature(Method)
        : Property is not null
            ? $"{PlcSimObjectProjector.TypeName(Property.PropertyType)} {Property.Name} {{ {(Property.CanRead ? "get; " : string.Empty)}{(Property.CanWrite ? "set; " : string.Empty)}}}"
            : DataMember is not null
                ? $"{PlcSimObjectProjector.TypeName(PlcSimObjectProjector.MemberType(DataMember))} {DataMember.Name} ({(DataMember is FieldInfo ? "field" : "property")})"
                : null;

    public static string MethodSignature(MethodInfo method)
        => $"{PlcSimObjectProjector.TypeName(method.ReturnType)} {method.Name}({string.Join(", ", method.GetParameters().Select(parameter =>
            $"{PlcSimObjectProjector.TypeName(parameter.ParameterType)} {parameter.Name}{(parameter.HasDefaultValue ? " = " + (parameter.DefaultValue ?? "null") : string.Empty)}"))})";
}

/// <summary>
/// The catalog resolved once against a loaded PLCSIM Advanced assembly (or, in tests, against
/// stand-in types supplied by the same lookup). The lookup is the only way a type is found.
/// </summary>
internal sealed class PlcSimApiSurface
{
    private readonly Dictionary<PlcSimMemberSpec, PlcSimResolvedMember> _members = new();
    private readonly Func<string, Type?> _lookup;

    private PlcSimApiSurface(Func<string, Type?> lookup)
    {
        _lookup = lookup;
        ManagerType = Find(PlcSimApiCatalog.ManagerTypeName);
        InstanceType = Find(PlcSimApiCatalog.InstanceTypeName);
        RuntimeExceptionType = Find(PlcSimApiCatalog.RuntimeExceptionTypeName);
        InitializationExceptionType = Find(PlcSimApiCatalog.InitializationExceptionTypeName);
        ErrorCodeType = Find(PlcSimApiCatalog.ErrorCodeTypeName);
    }

    public Type? ManagerType { get; }

    public Type? InstanceType { get; }

    public Type? RuntimeExceptionType { get; }

    public Type? InitializationExceptionType { get; }

    public Type? ErrorCodeType { get; }

    /// <summary>Element type of RegisteredInstanceInfo (SInstanceInfo), discovered from the property type.</summary>
    public Type? InstanceInfoType { get; private set; }

    /// <summary>Element type of TagInfos (STagInfo), discovered from the property type.</summary>
    public Type? TagInfoType { get; private set; }

    public Func<string, Type?> Lookup => _lookup;

    /// <summary>
    /// Sticky: a type initializer of the loaded API failed in this process. .NET caches that failure
    /// for the process lifetime, so only a new worker process can retry.
    /// </summary>
    public bool InitializationFailed { get; private set; }

    internal void MarkInitializationFailed() => InitializationFailed = true;

    public PlcSimResolvedMember this[PlcSimMemberSpec spec] => _members[spec];

    public IEnumerable<PlcSimResolvedMember> All => PlcSimApiCatalog.Members.Select(spec => _members[spec]);

    public static PlcSimApiSurface Resolve(Func<string, Type?> lookup)
    {
        var surface = new PlcSimApiSurface(lookup);
        foreach (var spec in PlcSimApiCatalog.Members.Where(spec => spec.Kind != PlcSimMemberKind.DataMember))
            surface._members[spec] = surface.ResolveMember(spec);
        surface.InstanceInfoType = ElementType(surface._members[PlcSimApiCatalog.RegisteredInstanceInfo].Property?.PropertyType);
        surface.TagInfoType = ElementType(surface._members[PlcSimApiCatalog.TagInfos].Property?.PropertyType);
        surface._members[PlcSimApiCatalog.TagInfoName] = surface.ResolveDataMember(PlcSimApiCatalog.TagInfoName, surface.TagInfoType,
            "the element type of IInstance.TagInfos");
        return surface;
    }

    /// <summary>Returns the resolved member or throws <c>plcsim_api_member_missing</c> naming it.</summary>
    public PlcSimResolvedMember Require(PlcSimMemberSpec spec)
    {
        var member = _members[spec];
        if (member.Resolved)
            return member;
        throw PlcSimOperationException.MemberMissing(spec.Id,
            $"The loaded S7-PLCSIM Advanced API does not expose {spec.Id} with a declared signature "
            + $"({string.Join("; ", spec.CandidateDisplays)}): {member.Detail}. Run plcsim_read probe_api for the resolved API surface.");
    }

    /// <summary>The exception member typed ERuntimeErrorCode, when exactly one exists.</summary>
    public (MemberInfo? Member, string? Problem) ErrorCodeMember(Type exceptionType)
    {
        if (ErrorCodeType is null)
            return (null, $"{PlcSimApiCatalog.Namespace}.{PlcSimApiCatalog.ErrorCodeTypeName} was not found");
        var members = PlcSimObjectProjector.DataMembers(exceptionType)
            .Where(member => PlcSimObjectProjector.MemberType(member) == ErrorCodeType)
            .ToList();
        return members.Count switch
        {
            1 => (members[0], null),
            0 => (null, $"{PlcSimObjectProjector.TypeName(exceptionType)} has no public field or property of type {PlcSimApiCatalog.ErrorCodeTypeName}"),
            _ => (null, $"{PlcSimObjectProjector.TypeName(exceptionType)} has several members of type {PlcSimApiCatalog.ErrorCodeTypeName}: {string.Join(", ", members.Select(member => member.Name))}")
        };
    }

    /// <summary>
    /// Whether the loaded <c>ERuntimeErrorCode</c> enum has a member named exactly
    /// <see cref="PlcSimApiCatalog.ErrorCodeOkMemberName"/>; null when the enum was not found.
    /// </summary>
    public bool? HasOkMember => ErrorCodeType is { IsEnum: true } codeType
        ? Enum.GetNames(codeType).Contains(PlcSimApiCatalog.ErrorCodeOkMemberName, StringComparer.Ordinal)
        : null;

    /// <summary>
    /// Judges a write or control return value. The loaded <c>ERuntimeErrorCode</c> enum succeeds with
    /// the value of its member named exactly <c>OK</c> when it has one; only without such a member is
    /// zero assumed to be success (<c>ZeroFallback</c>; the native success code is <c>SREC_OK</c>,
    /// whose value is not documented in the obtainable reference). Any other value is judged by its
    /// sign, as the Siemens developer reference (API 8.0) describes the native codes ("Errors are
    /// represented by negative values, warnings by positive values"): negative is an error, positive
    /// a warning (success). Zero that is not the <c>OK</c> value, and any non-success value of an
    /// unsigned enum, cannot be judged. A <c>Boolean</c> succeeds only with <c>true</c>; every other
    /// return type is not interpreted. <c>Code</c> is the member name read from the loaded enum (or
    /// the number when undefined); <c>Value</c> its numeric value (null for an unsigned enum).
    /// </summary>
    public (string Check, string? Code, long? Value) InterpretReturn(Type returnType, object? returned)
        => InterpretReturn(returnType, returned, out _);

    public (string Check, string? Code, long? Value) InterpretReturn(Type returnType, object? returned, out bool zeroFallback)
    {
        zeroFallback = false;
        if (returnType == typeof(bool))
            return returned is true ? (PlcSimReturnChecks.Ok, null, null) : (PlcSimReturnChecks.Failed, bool.FalseString, null);
        if (ErrorCodeType is not { IsEnum: true } codeType || returnType != codeType || returned is null)
            return (PlcSimReturnChecks.NotInterpreted, null, null);
        var code = Enum.GetName(codeType, returned) ?? Convert.ToString(returned, System.Globalization.CultureInfo.InvariantCulture);
        var underlying = Enum.GetUnderlyingType(codeType);
        var unsigned = underlying == typeof(byte) || underlying == typeof(ushort) || underlying == typeof(uint) || underlying == typeof(ulong);
        long? value = unsigned ? null : Convert.ToInt64(returned, System.Globalization.CultureInfo.InvariantCulture);
        var isZero = unsigned
            ? Convert.ToUInt64(returned, System.Globalization.CultureInfo.InvariantCulture) == 0
            : value == 0;
        if (HasOkMember == true)
        {
            if (Equals(returned, Enum.Parse(codeType, PlcSimApiCatalog.ErrorCodeOkMemberName, ignoreCase: false)))
                return (PlcSimReturnChecks.Ok, code, value);
        }
        else if (isZero)
        {
            zeroFallback = true;
            return (PlcSimReturnChecks.Ok, code, value);
        }

        if (unsigned || isZero)
            return (PlcSimReturnChecks.Uninterpretable, code, value);
        return (value < 0 ? PlcSimReturnChecks.Failed : PlcSimReturnChecks.Warning, code, value);
    }

    public bool IsRuntimeException(Exception exception) => IsOrDerives(exception.GetType(), RuntimeExceptionType);

    public bool IsInitializationException(Exception exception) => IsOrDerives(exception.GetType(), InitializationExceptionType);

    private static bool IsOrDerives(Type type, Type? expected)
        => expected is not null && (type == expected || type.IsSubclassOf(expected));

    private Type? Find(string simpleName) => _lookup(PlcSimApiCatalog.Namespace + "." + simpleName);

    private static Type? ElementType(Type? collectionType)
    {
        if (collectionType is null)
            return null;
        if (collectionType.IsArray)
            return collectionType.GetElementType();
        var enumerable = new[] { collectionType }.Concat(collectionType.GetInterfaces())
            .FirstOrDefault(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        return enumerable?.GetGenericArguments()[0];
    }

    private PlcSimResolvedMember ResolveMember(PlcSimMemberSpec spec)
    {
        var owner = spec.Owner == PlcSimApiCatalog.ManagerTypeName ? ManagerType : InstanceType;
        if (owner is null)
            return new(spec, $"type {PlcSimApiCatalog.Namespace}.{spec.Owner} was not found in the loaded assembly");

        var isStatic = spec.Kind is PlcSimMemberKind.StaticProperty or PlcSimMemberKind.StaticMethod;
        var flags = BindingFlags.Public | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
        var searched = owner.IsInterface && !isStatic ? new[] { owner }.Concat(owner.GetInterfaces()).ToArray() : new[] { owner };

        if (spec.Kind is PlcSimMemberKind.StaticProperty or PlcSimMemberKind.InstanceProperty)
        {
            var properties = searched.SelectMany(type => type.GetProperties(flags))
                .Where(property => string.Equals(property.Name, spec.Name, StringComparison.Ordinal) && property.GetIndexParameters().Length == 0)
                .Distinct()
                .ToList();
            if (properties.Count == 0)
                return new(spec, $"no public {(isStatic ? "static" : "instance")} property named {spec.Name} on {spec.Owner}");
            if (properties.Count > 1)
                return new(spec, $"{properties.Count} properties named {spec.Name} were found (ambiguous)");
            var property = properties[0];
            if (!property.CanRead || property.GetGetMethod() is null)
                return new(spec, $"{spec.Name} has no public getter");
            if (spec.NeedsSetter && (!property.CanWrite || property.GetSetMethod() is null))
                return new(spec, $"{spec.Name} has no public setter");
            return new(spec, null) { Property = property };
        }

        var methods = searched.SelectMany(type => type.GetMethods(flags))
            .Where(method => string.Equals(method.Name, spec.Name, StringComparison.Ordinal) && !method.IsGenericMethodDefinition)
            .Distinct()
            .ToList();
        if (methods.Count == 0)
            return new(spec, $"no public {(isStatic ? "static" : "instance")} method named {spec.Name} on {spec.Owner}");

        foreach (var candidate in spec.Candidates)
        {
            var matches = Match(methods, candidate);
            if (matches.Count == 1)
                return new(spec, null) { Method = matches[0], Candidate = candidate };
            if (matches.Count > 1)
                return new(spec, $"candidate {candidate.Display(spec.Name)} matches {matches.Count} overloads (ambiguous): "
                    + string.Join("; ", matches.Select(PlcSimResolvedMember.MethodSignature)));
        }

        return new(spec, "no overload matches a declared candidate; found: "
            + string.Join("; ", methods.Select(PlcSimResolvedMember.MethodSignature)));
    }

    private List<MethodInfo> Match(IReadOnlyList<MethodInfo> methods, PlcSimSignature candidate)
    {
        if (candidate.CallEquivalent)
        {
            var parameterless = methods.Where(method => method.GetParameters().Length == 0).ToList();
            if (parameterless.Count > 0)
                return parameterless;
            return methods.Where(method => method.GetParameters()
                .All(parameter => parameter.IsOptional && parameter.HasDefaultValue && !parameter.ParameterType.IsByRef)).ToList();
        }

        var expected = candidate.Parameters.Select(parameter => (parameter.ByRef, Type: parameter.Resolve(_lookup))).ToList();
        if (expected.Any(parameter => parameter.Type is null))
            return new List<MethodInfo>();
        return methods.Where(method =>
        {
            var parameters = method.GetParameters();
            if (parameters.Length != expected.Count)
                return false;
            for (var index = 0; index < parameters.Length; index++)
            {
                var actual = parameters[index].ParameterType;
                var (byRef, type) = expected[index];
                if (byRef ? !actual.IsByRef || actual.GetElementType() != type : actual != type)
                    return false;
            }

            return candidate.ReturnType is null || method.ReturnType == candidate.ReturnType;
        }).ToList();
    }

    private PlcSimResolvedMember ResolveDataMember(PlcSimMemberSpec spec, Type? owner, string ownerDescription)
    {
        if (owner is null)
            return new(spec, $"{ownerDescription} is unknown because the collection member did not resolve");
        var members = PlcSimObjectProjector.DataMembers(owner)
            .Where(member => string.Equals(member.Name, spec.Name, StringComparison.Ordinal))
            .ToList();
        if (members.Count == 0)
            return new(spec, $"{PlcSimObjectProjector.TypeName(owner)} ({ownerDescription}) has no public field or property named {spec.Name}");
        var memberType = PlcSimObjectProjector.MemberType(members[0]);
        if (memberType != spec.DataMemberType)
            return new(spec, $"{PlcSimObjectProjector.TypeName(owner)}.{spec.Name} has type {PlcSimObjectProjector.TypeName(memberType)}, not {PlcSimObjectProjector.TypeName(spec.DataMemberType!)}");
        return new(spec, null) { DataMember = members[0] };
    }
}
