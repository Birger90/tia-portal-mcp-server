using System.Globalization;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using TiaMcpServer.Contracts.Plc.Settings;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.PlcOnline;
using TiaMcpServer.OpennessWorker.Openness.TechnologyObjects;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Plc.Settings;

/// <summary>
/// The settings family (issues #15, #13, #12, #14, #21, #25; not live-tested). Nothing here is compiled against a Siemens
/// settings type: this class only supplies the live objects (project, CPU device item, its device items, PLC software, watch and
/// force tables, active languages) and the documented <c>GetServiceInfos()</c>/<c>GetService(Type)</c> lookups to the
/// Siemens-free <see cref="PlcSettingsRun"/>, which binds every member by exact name. Passwords come only from password
/// sources and are never generated, returned or logged.
/// </summary>
internal static class PlcSettingsService
{
    /// <summary>A getter failure that only makes a value unreadable; session loss and disposal still propagate.</summary>
    private static bool Recoverable(Exception exception)
        => exception is EngineeringException engineering && OpennessExceptions.IsRecoverable(engineering)
            && engineering is not EngineeringObjectDisposedException;

    public static PlcSettingsReadInfo Read(ProjectBase project, string method, PlcSettingsRequestInfo info)
    {
        var contexts = new List<PlcSettingsContext>();
        if (!PlcSettingsCatalog.IsProjectScoped(method))
        {
            var failures = new List<string>();
            var plcs = PlcSoftwareLocator.FindEveryPlc(project, info.PlcName, failures).ToList();
            if (failures.Count > 0)
                throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, failures[0] + " Nothing was read.");
            if (plcs.Count == 0 && info.PlcName is not null)
                throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No PLC software named '{info.PlcName}' was found in the project.");
            contexts.AddRange(plcs.Select(plc => Context(project, plc)));
        }

        return Bind(() => PlcSettingsRun.Read(method, contexts, Context(project, null), info));
    }

    public static (PlcSettingsResultInfo Result, List<string> Warnings) Write(ProjectBase project, string method, PlcSettingsRequestInfo info)
    {
        var context = PlcSettingsCatalog.IsProjectScoped(method) ? Context(project, null) : Context(project, PlcSoftwareLocator.FindUnique(project, info.PlcName));
        return Bind(() => PlcSettingsRun.Write(method, context, info));
    }

    private static PlcSettingsContext Context(ProjectBase project, PlcSoftwareLocator.DiscoveredPlcSoftware? plc)
    {
        var cpu = plc?.HostDeviceItem;
        if (plc is not null && cpu is null)
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                $"The CPU device item of PLC '{plc.Software.Name}' is unknown. Nothing was changed.");
        var context = new PlcSettingsContext
        {
            PlcName = plc?.Software.Name,
            DeviceName = plc?.DeviceName,
            Project = project as global::Siemens.Engineering.Project,
            Cpu = cpu,
            Software = plc?.Software,
            CpuItems = cpu is null ? Array.Empty<object>() : Descendants(cpu).Cast<object>().ToList(),
            ServiceTypes = ServiceTypes,
            // K76: GetService(Type), then the documented generic GetService<T>() closed over the listed type.
            Service = TiaMcpServer.Contracts.Shared.ListedServices.Retrieve,
            NameOf = NameOf,
            Recoverable = Recoverable,
            // Password sources resolve in the worker only (environment of the worker process, or Windows generic credentials).
            Resolve = PlcSettingsRun.Resolver(Environment.GetEnvironmentVariable, WindowsCredentialReader.Instance),
            ActiveCulture = culture => ActiveCulture(project, culture),
        };
        if (plc is not null)
        {
            context.Table = (name, folder, force) => Table(project, plc.Software.Name, name, folder, force);
            context.DeviceItem = name => FindDeviceItem(project, plc.DeviceName, name);
            context.Device = DeviceOf(project, plc.DeviceName);
        }

        return context;
    }

    /// <summary>GetServiceInfos() of an engineering object; an object that lists none (or is no service provider) offers nothing.</summary>
    private static IReadOnlyList<Type> ServiceTypes(object owner)
    {
        if (owner is not IEngineeringServiceProvider) return Array.Empty<Type>();
        try
        {
            return TechnologyObjectRuntime.ServiceTypes(owner);
        }
        catch (WorkerOperationException exception) when (exception.FailureCategory == WorkerFailureCategories.UnsupportedCapability)
        {
            return Array.Empty<Type>();
        }
    }

    private static string? NameOf(object? value)
    {
        try
        {
            return value switch
            {
                null => null,
                DeviceItem item => item.Name,
                Device device => device.Name,
                IEngineeringObject engineering => PlcSettingsContext.DefaultName(engineering),
                _ => PlcSettingsContext.DefaultName(value),
            };
        }
        catch (EngineeringException exception) when (Recoverable(exception))
        {
            return null;
        }
    }

    private static IEnumerable<DeviceItem> Descendants(DeviceItem item)
    {
        foreach (DeviceItem child in item.DeviceItems)
        {
            yield return child;
            foreach (var grandchild in Descendants(child)) yield return grandchild;
        }
    }

    /// <summary>The PLC's device by the name the locator reported; null when it is not unique.</summary>
    private static Device? DeviceOf(ProjectBase project, string deviceName)
    {
        var devices = Project.ProjectDeviceEnumerator.Enumerate(project).Where(d => string.Equals(d.Name, deviceName, StringComparison.Ordinal)).Take(2).ToList();
        return devices.Count == 1 ? devices[0] : null;
    }

    /// <summary>A device item of the PLC's device (by the device name the locator reported) by exact name (case-sensitive, unique), or null.</summary>
    private static object? FindDeviceItem(ProjectBase project, string deviceName, string name)
    {
        var devices = Project.ProjectDeviceEnumerator.Enumerate(project).Where(d => string.Equals(d.Name, deviceName, StringComparison.Ordinal)).Take(2).ToList();
        if (devices.Count != 1) return null;
        var device = devices[0];
        var matches = device.DeviceItems.Cast<DeviceItem>().SelectMany(item => new[] { item }.Concat(Descendants(item)))
            .Where(item => string.Equals(item.Name, name, StringComparison.Ordinal)).Take(2).ToList();
        if (matches.Count > 1)
            throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous, $"Device '{device.Name}' has several device items named '{name}'. Nothing was changed.");
        return matches.FirstOrDefault();
    }

    private static object Table(ProjectBase project, string plcName, string name, string? folder, bool force)
    {
        var (_, group, path) = PlcWatchTableService.ResolveGroup(project, plcName, folder);
        return force ? PlcWatchTableService.ForceTable(group, path, name) : PlcWatchTableService.WatchTable(group, path, name);
    }

    /// <summary>The <c>Language.Culture</c> of the active project language named <paramref name="culture"/> (the manual's argument), or null.</summary>
    private static object? ActiveCulture(ProjectBase project, string culture)
    {
        foreach (Language language in project.LanguageSettings.ActiveLanguages)
            if (string.Equals(language.Culture.Name, culture, StringComparison.OrdinalIgnoreCase)) return language.Culture;
        return null;
    }

    /// <summary>Translates a Siemens-free refusal into the worker's categorized failure; Siemens exceptions pass unchanged.</summary>
    private static T Bind<T>(Func<T> run)
    {
        try
        {
            return run();
        }
        catch (RuntimeBindingException exception)
        {
            throw new WorkerOperationException(exception.Category, exception.Message);
        }
    }

    internal static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);
}
