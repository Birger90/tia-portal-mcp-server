using System.ComponentModel;

namespace TiaMcpServer.Plc;

/// <summary>The fields of plc_write generate_loadable (issue #26); blockPaths, filePath, overwrite and plcName are shared.</summary>
public sealed partial class PlcOperationRequest
{
    [Description("generate_loadable: the software units to generate the loadable file for (PlcUnit names, LoadableProvider.GenerateLoadable(FileInfo, IEnumerable<PlcUnit>, TargetOption)); exactly one of unitNames or blockPaths, 1-200 distinct names.")]
    public IReadOnlyList<string>? UnitNames { get; set; }

    [Description("generate_loadable: the download target of the loadable file, a TargetOption member name: Plc or PlcSim (None is documented to throw and is not offered). Required.")]
    public string? Target { get; set; }
}
