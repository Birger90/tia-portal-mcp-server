using System.ComponentModel;

namespace TiaMcpServer.Plc;

/// <summary>The fields of plc_write set_project_compilation_settings (K10).</summary>
public sealed partial class PlcOperationRequest
{
    [Description("set_project_compilation_settings: \"Simulation während Bausteinübersetzung unterstützen\" (Support simulation during block "
        + "compilation, project properties), PlcSimulationSettingsProvider.IsSimulationDuringBlockCompilationEnabled; true or false. Siemens' "
        + "PLCSIM Advanced getting-started asks for it before a Softbus download. Omitted leaves it unchanged.")]
    public bool? SimulationDuringBlockCompilation { get; set; }

    [Description("set_project_compilation_settings: support for a virtual PLC during block compilation, "
        + "VirtualPlcSettingsProvider.IsVirtualPlcDuringBlockCompilationEnabled; true or false. Omitted leaves it unchanged.")]
    public bool? VirtualPlcDuringBlockCompilation { get; set; }
}
