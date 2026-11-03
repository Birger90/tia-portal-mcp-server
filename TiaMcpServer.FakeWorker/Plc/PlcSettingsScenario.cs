using System.Globalization;
using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Settings;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using Hw = Siemens.Engineering.HW;
using Features = Siemens.Engineering.HW.Features;
using Security = Siemens.Engineering.Security;
using Tables = Siemens.Engineering.SW.WatchAndForceTables;

namespace TiaMcpServer.FakeWorker.Plc;

/// <summary>
/// Stateful <c>plc-settings</c> fixture: PLC_1 (Station_1) whose CPU lists the issue #15 services as stand-ins (display services on
/// its submodule "Display"), plus the project's password policies and syslog servers, and runs the worker's own Siemens-free
/// <see cref="PlcSettingsRun"/> against them. Watch table "Watch_1" and force table "Force_1" exist in the root group; the CPU
/// has the attributes the manual documents (syslog, MultilingualSupport with two rows, PlcAccessCommunicationModule) and the
/// device holds a CP named "CP 1543-1". Active project languages: en-US, de-DE. Member shapes only; no evidence of Siemens behavior.
/// </summary>
sealed partial class PlcSettingsScenario
{
    private readonly Hw.FakeDeviceItem _cpu = new("PLC_1");
    private readonly Hw.FakeDeviceItem _display = new("Display");
    private readonly Hw.FakeDeviceItem _cp = new("CP 1543-1");
    private readonly object _project = new FakeProject();
    private readonly Dictionary<object, List<object>> _services = new(ReferenceEqualityComparer.Instance);
    private readonly Tables.PlcWatchTable _watch = new("Watch_1");
    private readonly Tables.PlcForceTable _force = new("Force_1");

    private sealed class FakeProject
    {
        public string Name => "Project1";
    }

    public PlcSettingsScenario()
    {
        _services[_cpu] = new List<object>
        {
            new Features.PlcAccountLockingAtRuntimeFeature(), new Features.SysLogConfigurationManager(), new Features.SystemWebPagesFeature(),
            new Features.DefaultWebPagesFeature(), new Features.WatchAndForceTableAccessManager(), new Features.WebserverUserDefinedPages(),
        };
        _services[_display] = new List<object> { new Features.DisplayProtection(), new Features.FrontPanelDisplay(), new Features.WatchAndForceTableAccessManager() };
        _services[_project] = new List<object> { new Security.PlcPasswordPolicyService(), new Security.LegacyPlcPasswordPolicyService(), new Security.SyslogServerProvider() };
        _cpu.Attributes["SysLogAutoAcceptClient"] = false;
        _cpu.Attributes["SysLogClientCertificateId"] = 0u;
        _cpu.Attributes["SysLogTrustedCertificateIds"] = string.Empty;
        var table = new Hw.CustomDataTypes.TableData();
        foreach (var display in new[] { "English", "German" })
        {
            var row = new Hw.CustomDataTypes.StructuredData();
            row.Attributes["ProjectLanguage"] = CultureInfo.GetCultureInfo("en-US");
            row.Attributes["DisplayLanguage"] = display;
            table.Rows.Add(row);
        }

        _cpu.Attributes["MultilingualSupport"] = table;
        _cpu.Attributes["PlcAccessCommunicationModule"] = null;
        InitializeMore();
    }

    partial void InitializeMore();

    private PlcSettingsContext Context(bool project)
    {
        var context = new PlcSettingsContext
        {
            Project = _project,
            ServiceTypes = owner => Services(owner).Select(s => s.GetType()).ToList(),
            Service = (owner, type) => Services(owner).FirstOrDefault(s => s.GetType() == type),
            Recoverable = exception => exception is InvalidOperationException,
            Resolve = PlcSettingsRun.Resolver(Environment.GetEnvironmentVariable, null),
            ActiveCulture = culture => culture is "en-US" or "de-DE" ? CultureInfo.GetCultureInfo(culture) : null,
        };
        if (project) return context;
        context.PlcName = "PLC_1";
        context.DeviceName = "Station_1";
        context.Cpu = _cpu;
        context.CpuItems = CpuItems();
        context.Table = (name, folder, force) => folder is not null && folder != "/"
            ? throw new RuntimeBindingException(WorkerFailureCategories.TargetNotFound, $"Watch-and-force table group '{folder}' was not found in PLC 'PLC_1'.")
            : force ? (string.Equals(name, _force.Name, StringComparison.OrdinalIgnoreCase) ? _force : throw NotFound("Force", name))
            : string.Equals(name, _watch.Name, StringComparison.OrdinalIgnoreCase) ? _watch : throw NotFound("Watch", name);
        context.DeviceItem = name => new object[] { _cpu, _display, _cp }.Concat(MoreDeviceItems()).FirstOrDefault(i => context.NameOf(i) == name);
        return context;
    }

    /// <summary>The stand-in services of an owner: registered ones, or those a stand-in carries itself (safety validation objects).</summary>
    private IEnumerable<object> Services(object owner)
        => _services.TryGetValue(owner, out var list) ? list : owner is Siemens.Engineering.SafetyValidation.IFakeServices carried ? carried.Services : Array.Empty<object>();

    private IReadOnlyList<object> CpuItems() => new object[] { _display }.Concat(MoreCpuItems()).ToList();

    private static RuntimeBindingException NotFound(string kind, string name) => new(WorkerFailureCategories.TargetNotFound, $"{kind} table '{name}' in group '/' was not found.");

    public string Handle(string line)
    {
        var request = JsonSerializer.Deserialize<WorkerRequest>(line, WorkerJson.Envelope)!;
        try
        {
            if (request.Method == "list_tag_tables")
                return Ok(WorkerJson.SerializePayload(new PlcTagInventoryInfo
                {
                    Plcs = { new PlcTagInventoryPlcInfo { PlcName = "PLC_1", DeviceName = "Station_1" } },
                }), null);
            var info = request.PlcSettings ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, $"{request.Method} requires the settings request.");
            var projectScoped = PlcSettingsCatalog.IsProjectScoped(request.Method);
            if (!projectScoped && info.PlcName is not null && !Same(info.PlcName, "PLC_1") && !Same(info.PlcName, "Station_1"))
                throw new RuntimeBindingException(WorkerFailureCategories.TargetNotFound, $"No PLC software named '{info.PlcName}' was found in the project.");
            if (PlcSettingsCatalog.IsRead(request.Method))
                return Ok(WorkerJson.SerializePayload(PlcSettingsRun.Read(request.Method, projectScoped ? Array.Empty<PlcSettingsContext>() : new[] { Context(false) }, Context(true), info)), null);
            if (!PlcSettingsCatalog.IsWrite(request.Method))
                throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, $"plc-settings scenario does not script '{request.Method}'.");
            var (result, warnings) = PlcSettingsRun.Write(request.Method, Context(projectScoped), info);
            return Ok(WorkerJson.SerializePayload(result), warnings);
        }
        catch (RuntimeBindingException ex)
        {
            return Fail(ex.Category, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            // Stands in for a TIA Portal rejection (EngineeringTargetInvocationException) of a mutation.
            return Fail(WorkerFailureCategories.EngineeringRejected, ex.Message);
        }
    }

    private static string Fail(string category, string message)
        => JsonSerializer.Serialize(new WorkerResponse { Success = false, FailureCategory = category, Error = message }, WorkerJson.Envelope);

    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string Ok(string payload, List<string>? warnings)
        => JsonSerializer.Serialize(new WorkerResponse { Success = true, Payload = payload, Warnings = warnings is { Count: > 0 } ? warnings : null }, WorkerJson.Envelope);
}
