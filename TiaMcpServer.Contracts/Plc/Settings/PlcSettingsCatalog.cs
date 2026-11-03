namespace TiaMcpServer.Contracts.Plc.Settings;

/// <summary>Shared names of the settings family.</summary>
public static class PlcSettingsNames
{
    public static class ReadBacks
    {
        /// <summary>Every change was read back and equals the request.</summary>
        public const string Matched = "matched";

        /// <summary>Nothing comparable can be read (a password, a file the service consumed, a call without a getter).</summary>
        public const string NotAvailable = "notAvailable";

        /// <summary>A getter exists but threw a recoverable error after the change.</summary>
        public const string Unreadable = "unreadable";

        public static bool IsKnown(string? value) => value is Matched or NotAvailable or Unreadable;
    }

    /// <summary>The most items a read reports per collection.</summary>
    public const int MaxItems = 500;

    /// <summary>The most settings one write takes.</summary>
    public const int MaxSettings = 20;

    public const string ProjectOwner = "project";

    public static string DeviceItemOwner(string name) => $"device item '{name}'";
}

/// <summary>
/// One operation of the settings family: its public name (also the worker method), whether it writes, the GitHub issue, whether
/// it is project-wide (no PLC), the documented service it uses, the property names its <c>settings</c> may carry (in the
/// order they are applied), the values of those settings that lower protection (info guard <c>plc_lowers_cpu_protection</c>)
/// and whether it raises protection (info guard <c>plc_raises_cpu_protection</c>).
/// </summary>
public sealed class PlcSettingsDefinition
{
    public PlcSettingsDefinition(string name, bool isWrite, int issue, bool projectScoped, string? service, string[]? settings = null,
        (string Setting, string Value)[]? lowers = null, bool raises = false, bool requiresSettings = false)
    {
        Name = name;
        IsWrite = isWrite;
        Issue = issue;
        ProjectScoped = projectScoped;
        Service = service;
        Settings = Array.AsReadOnly(settings ?? Array.Empty<string>());
        Lowers = Array.AsReadOnly(lowers ?? Array.Empty<(string, string)>());
        Raises = raises;
        RequiresSettings = requiresSettings;
    }

    public string Name { get; }

    public bool IsWrite { get; }

    public int Issue { get; }

    public bool ProjectScoped { get; }

    public string? Service { get; }

    public IReadOnlyList<string> Settings { get; }

    public IReadOnlyList<(string Setting, string Value)> Lowers { get; }

    public bool Raises { get; }

    /// <summary>The operation does nothing without at least one setting.</summary>
    public bool RequiresSettings { get; }

    /// <summary>A read that writes temporary files (OperationCapability.TemporaryExport instead of Observe).</summary>
    public bool Exports { get; init; }
}

/// <summary>
/// The operations of the settings family (issues #15, #13, #12, #14, #21, #25). Each name is the public plc_read/plc_write
/// operation and the worker method; the lists feed <c>OperationPolicyCatalog</c>, <c>ProjectCapabilityCatalog.OperationFor</c>
/// and the worker dispatch from this one place. Parameters travel in <c>WorkerRequest.PlcSettings</c>.
/// </summary>
public static partial class PlcSettingsCatalog
{
    private static readonly List<PlcSettingsDefinition> Definitions = new();

    private static PlcSettingsDefinition Add(PlcSettingsDefinition definition)
    {
        Definitions.Add(definition);
        return definition;
    }

    static PlcSettingsCatalog()
    {
        AddCpuSettings();
        AddUsers();
        AddUmac();
        AddCertificates();
        AddSafetyPrintout();
        AddSafetyValidation();
    }

    public static IReadOnlyList<PlcSettingsDefinition> All => Definitions;

    public static PlcSettingsDefinition? Find(string? name) => name is null ? null : Definitions.FirstOrDefault(d => d.Name == name);

    public static IReadOnlyList<string> Read => Definitions.Where(d => !d.IsWrite).Select(d => d.Name).ToArray();

    public static IReadOnlyList<string> Write => Definitions.Where(d => d.IsWrite).Select(d => d.Name).ToArray();

    /// <summary>The reads that write temporary files (TemporaryExport).</summary>
    public static IReadOnlyList<string> ExportingReads => Definitions.Where(d => !d.IsWrite && d.Exports).Select(d => d.Name).ToArray();

    public static bool IsRead(string? method) => Find(method) is { IsWrite: false };

    public static bool IsWrite(string? method) => Find(method) is { IsWrite: true };

    public static bool IsWorkerMethod(string? method) => Find(method) is not null;

    public static bool IsProjectScoped(string? method) => Find(method) is { ProjectScoped: true };
}
