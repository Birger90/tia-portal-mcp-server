using System.Reflection;
using System.Runtime.ExceptionServices;

namespace TiaMcpServer.Contracts.Project;

/// <summary>
/// The plc_write operation <c>set_project_compilation_settings</c> (K10): the two project-wide V21 block-compilation flags that
/// get_project_status reads as <c>compilationSettings</c>. The worker method carries the public name. Standalone projects only
/// (they are reached through <c>Project.GetService&lt;T&gt;()</c>, like the read).
/// </summary>
public static class ProjectCompilationSettingsMethods
{
    public const string Set = "set_project_compilation_settings";

    public static bool IsWorkerMethod(string? method) => method == Set;
}

/// <summary>
/// One setting: the wire field of the request, the V21 settings provider type (namespace <c>Siemens.Engineering.SW</c>) and
/// the boolean member on it. Evidence: the V21 member list of <c>Siemens.Engineering.SW.PlcSimulationSettingsProvider</c>
/// ("IsSimulationDuringBlockCompilationEnabled: To indicate whether Support for Simulation during block compilation is
/// enabled for the project") and <c>VirtualPlcSettingsProvider</c> (IsVirtualPlcDuringBlockCompilationEnabled); the TIA UI
/// checkbox is "Simulation während Bausteinübersetzung unterstützen" (project properties, Protection). Whether the members
/// are writable is not documented for V21 (the V20 Project properties of the same names were read/write), so the write is
/// bound on the live provider (<see cref="ProjectCompilationSettingBinding"/>).
/// </summary>
public static class ProjectCompilationSettingNames
{
    public const string SimulationField = "simulationDuringBlockCompilation";
    public const string VirtualPlcField = "virtualPlcDuringBlockCompilation";

    public const string SimulationProvider = "PlcSimulationSettingsProvider";
    public const string VirtualPlcProvider = "VirtualPlcSettingsProvider";

    public const string SimulationMember = "IsSimulationDuringBlockCompilationEnabled";
    public const string VirtualPlcMember = "IsVirtualPlcDuringBlockCompilationEnabled";

    public static string ProviderFor(string field) => field == SimulationField ? SimulationProvider : VirtualPlcProvider;

    public static string MemberFor(string field) => field == SimulationField ? SimulationMember : VirtualPlcMember;
}

/// <summary>The worker parameters of set_project_compilation_settings: null leaves a setting unchanged; at least one is set.</summary>
public sealed class ProjectCompilationSettingsRequestInfo
{
    public bool? SimulationDuringBlockCompilation { get; set; }

    public bool? VirtualPlcDuringBlockCompilation { get; set; }
}

/// <summary>The result of set_project_compilation_settings: one entry per requested setting, in request order.</summary>
public sealed class ProjectCompilationSettingsResultInfo
{
    public string Operation { get; set; } = ProjectCompilationSettingsMethods.Set;

    public List<ProjectCompilationSettingResultInfo> Settings { get; set; } = new();
}

public sealed class ProjectCompilationSettingResultInfo
{
    /// <summary><see cref="ProjectCompilationSettingNames.SimulationField"/> or <see cref="ProjectCompilationSettingNames.VirtualPlcField"/>.</summary>
    public string Setting { get; set; } = string.Empty;

    /// <summary>The provider type short name.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>The boolean member that was set and read back.</summary>
    public string Member { get; set; } = string.Empty;

    public bool Requested { get; set; }

    /// <summary>The value read before the write; null when it could not be read.</summary>
    public bool? Before { get; set; }

    /// <summary>The value read back after the write (the same member, read the way get_project_status reads it); null when unreadable.</summary>
    public bool? After { get; set; }

    /// <summary><see cref="ProjectCompilationSettingBinding.Routes"/>: how the write was bound on the live provider.</summary>
    public string Route { get; set; } = string.Empty;

    /// <summary>The bound member as text (for example <c>Boolean IsSimulationDuringBlockCompilationEnabled { set; }</c>).</summary>
    public string Signature { get; set; } = string.Empty;
}

/// <summary>A required member is missing or not writable on the live object; nothing was changed by the binding.</summary>
public sealed class ProjectCompilationSettingMissingException : Exception
{
    public ProjectCompilationSettingMissingException(string member, string message) : base(message) => Member = member;

    public string Member { get; }
}

/// <summary>
/// How one boolean compilation setting is written on the live provider, bound in this order without calling anything:
/// (i) a public writable instance property of exactly the member name and type <see cref="bool"/> (the same property the read
/// uses); (ii) an attribute of exactly that name that <c>GetAttributeInfos()</c> lists as writable, set with
/// <c>SetAttribute(name, bool)</c>. Neither: <see cref="ProjectCompilationSettingMissingException"/> (the worker reports
/// <c>api_member_missing</c>). The read-back always uses the property getter, the mechanism get_project_status reads with.
/// Siemens-free; shared by the worker and the tests.
/// </summary>
public static class ProjectCompilationSettingBinding
{
    public static class Routes
    {
        public const string Property = "property";
        public const string SetAttribute = "setAttribute";
    }

    /// <summary>The generic attribute surface of the live object (IEngineeringObject); null means it has none.</summary>
    public sealed class AttributeSurface(Func<IEnumerable<(string Name, bool Writable)>> infos, Action<string, object> set)
    {
        public Func<IEnumerable<(string Name, bool Writable)>> Infos { get; } = infos;

        public Action<string, object> Set { get; } = set;
    }

    public sealed class Assignment(string member, string route, string signature, Action<bool> apply, Func<bool?> read)
    {
        public string Member { get; } = member;

        public string Route { get; } = route;

        public string Signature { get; } = signature;

        /// <summary>Writes the value. A Siemens exception escapes unwrapped for the shared classification.</summary>
        public void Apply(bool value) => apply(value);

        /// <summary>Reads the property again; null when there is no readable boolean property.</summary>
        public bool? Read() => read();
    }

    /// <exception cref="ProjectCompilationSettingMissingException">Neither route exists on the live object.</exception>
    public static Assignment Bind(object provider, string member, AttributeSurface? attributes)
    {
        if (provider is null) throw new ArgumentNullException(nameof(provider));
        var properties = provider.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => string.Equals(p.Name, member, StringComparison.Ordinal) && p.GetIndexParameters().Length == 0).ToList();
        var property = properties.Count == 1 && properties[0].PropertyType == typeof(bool) ? properties[0] : null;
        Func<bool?> read = property is { CanRead: true } readable && readable.GetGetMethod() is not null
            ? () => (bool?)Unwrap(() => readable.GetValue(provider, null))
            : () => null;
        if (property is { CanWrite: true } && property.GetSetMethod() is not null)
            return new Assignment(member, Routes.Property, $"Boolean {member} {{ set; }}",
                value => Unwrap(() => { property.SetValue(provider, value, null); return null; }), read);

        string note;
        if (attributes is null)
        {
            note = "the object offers no GetAttributeInfos()";
        }
        else
        {
            var listed = attributes.Infos().Where(info => string.Equals(info.Name, member, StringComparison.Ordinal)).ToList();
            if (listed.Count == 1 && listed[0].Writable)
                return new Assignment(member, Routes.SetAttribute, $"IEngineeringObject.SetAttribute(\"{member}\", Boolean)",
                    value => attributes.Set(member, value), read);
            note = listed.Count == 0 ? $"GetAttributeInfos() lists no attribute '{member}'"
                : listed.Count > 1 ? $"GetAttributeInfos() lists '{member}' more than once" : $"GetAttributeInfos() lists '{member}' as not writable";
        }

        var described = properties.Count == 0 ? "has no public property"
            : property is null ? "has no single public Boolean property"
            : "has a read-only property";
        throw new ProjectCompilationSettingMissingException(member,
            $"{provider.GetType().Name} {described} '{member}' with a public setter, and {note}. Nothing was changed.");
    }

    private static object? Unwrap(Func<object?> call)
    {
        try
        {
            return call();
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
}
