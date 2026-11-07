using Hw = Siemens.Engineering.HW;
using Features = Siemens.Engineering.HW.Features;

namespace TiaMcpServer.FakeWorker.Plc;

/// <summary>The stand-ins of the settings operations beyond issue #15: the OPC UA submodule (issue #13).</summary>
sealed partial class PlcSettingsScenario
{
    private readonly Hw.FakeDeviceItem _opcUa = new("OPC UA");

    partial void InitializeMore()
    {
        _services[_cpu].Add(new Features.WebserverUserManagement());
        _services[_cpu].Add(new Features.SimpleWebserverUserManagement());
        _services[_opcUa] = new List<object> { new Features.OpcUaUserManagement() };
        var umac = new Siemens.Engineering.Umac.UmacConfigurator();
        _services[_project].Add(umac);
        _services[_project].Add(new Siemens.Engineering.Umac.UmcServerConfigurator());
        ((FakeProject)_project).Protect = (name, password) => umac.ProjectUsers.Create(name, password);
        _services[_project].Add(new Siemens.Engineering.Umac.PasswordPolicyConfigurator());
        _services[_cpu].Add(new Siemens.Engineering.Umac.UmacDevice());
        _services[_cpu].Add(new Siemens.Engineering.Security.LocalCertificateManager());
        _services[_cpu].Add(new Features.CertificateManagementConfiguration());
        _services[_cpu].Add(new Siemens.Engineering.Safety.SafetyPrintout());
        _services[_project].Add(new Siemens.Engineering.SafetyValidation.SafetyValidationAssistant(
            new List<Hw.FakeDeviceItem> { _cpu }, new List<Hw.FakeDeviceItem> { new("F-DI 8x24VDC"), new("F-DQ 4x24VDC") }));
        _cpu.Attributes["WebserverCertificate"] = null;
        _opcUa.Attributes["OpcUaServerCertificate"] = null;
    }

    private IEnumerable<object> MoreDeviceItems() => new object[] { _opcUa };

    private IEnumerable<object> MoreCpuItems() => new object[] { _opcUa };
}
