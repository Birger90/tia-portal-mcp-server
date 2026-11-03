// Origin: feature idea from heilingbrunner/tiaportal-mcp (MIT, Copyright (c) 2025 J.Heilingbrunner), commit f4eb4d7
// ("feat: add ExportOpcUaXml tool", 2026-10-19). Reimplemented here; no code copied. The Siemens members come from the
// Siemens V21 reference as rendered in Czarnak/totally-integrated-claude (MIT): skills/tia-import-export/references/plc-blocks.md
// §13 and skills/tia-devices-general/references/device-item-operations.md (OpcUaExportProvider.Export(DeviceItem, FileInfo)),
// and api-baselines/v21/base.json (type Siemens.Engineering.HW.Utilities.OpcUaExportProvider). See THIRD_PARTY_NOTICES.md.
using System.Reflection;

namespace TiaMcpServer.Contracts.Plc;

/// <summary>
/// export_opcua_xml: the OPC UA data of one PLC as an XML file (TIA Portal's "Export OPC UA" of a CPU), through
/// <c>Project.HwUtilities.Find("OPCUAExportProvider")</c> and <c>OpcUaExportProvider.Export(DeviceItem, FileInfo)</c>.
/// No stub declares the provider: the worker binds it at runtime with the exact names and parameter types below and
/// reports <c>api_member_missing</c> when the installed TIA Portal does not offer them. Siemens-free so the binding rule
/// is tested without TIA Portal.
/// </summary>
public static class PlcOpcUaExport
{
    /// <summary>The identifier the Siemens example passes to <c>HwUtilities.Find</c>.</summary>
    public const string ProviderId = "OPCUAExportProvider";

    /// <summary>The documented provider type (V21 API baseline).</summary>
    public const string ProviderTypeName = "Siemens.Engineering.HW.Utilities.OpcUaExportProvider";

    public const string ExportMethodName = "Export";

    public const string DeviceItemTypeName = "Siemens.Engineering.HW.DeviceItem";

    /// <summary>True when <paramref name="type"/> or one of its base types is exactly <see cref="ProviderTypeName"/>.</summary>
    public static bool IsProvider(Type? type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            if (string.Equals(current.FullName, ProviderTypeName, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>
    /// The one public instance <c>Export(DeviceItem, FileInfo)</c> of <paramref name="providerType"/>: exactly two
    /// parameters, the first a <see cref="DeviceItemTypeName"/> (or a type deriving from it), the second
    /// <see cref="FileInfo"/>. Null with the reason when there is none or more than one.
    /// </summary>
    public static (MethodInfo? Method, string? Missing) BindExport(Type providerType)
    {
        if (providerType is null) throw new ArgumentNullException(nameof(providerType));
        var candidates = providerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => string.Equals(method.Name, ExportMethodName, StringComparison.Ordinal))
            .Where(method =>
            {
                var parameters = method.GetParameters();
                return parameters.Length == 2 && IsDeviceItem(parameters[0].ParameterType) && parameters[1].ParameterType == typeof(FileInfo);
            })
            .ToArray();
        return candidates.Length switch
        {
            1 => (candidates[0], null),
            0 => (null, $"{providerType.FullName} offers no public Export({DeviceItemTypeName}, System.IO.FileInfo)."),
            _ => (null, $"{providerType.FullName} offers {candidates.Length} public Export(DeviceItem, FileInfo) overloads; the call is ambiguous."),
        };
    }

    private static bool IsDeviceItem(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            if (string.Equals(current.FullName, DeviceItemTypeName, StringComparison.Ordinal)) return true;
        return false;
    }
}

/// <summary>Result of export_opcua_xml: the PLC, its CPU device item and the written file, re-read after it was placed.</summary>
public class PlcOpcUaExportResultInfo
{
    public string Operation { get; set; } = PlcBlockEditMethods.ExportOpcUaXml;

    public string PlcName { get; set; } = string.Empty;

    /// <summary>The device holding the PLC software.</summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>The CPU device item handed to <c>OpcUaExportProvider.Export</c>.</summary>
    public string DeviceItemName { get; set; } = string.Empty;

    /// <summary>The caller's path as the worker resolved it.</summary>
    public string FilePath { get; set; } = string.Empty;

    public long FileBytes { get; set; }

    /// <summary>Lowercase hex SHA-256 of the written file.</summary>
    public string FileSha256 { get; set; } = string.Empty;

    /// <summary>An existing file was replaced (overwrite:true).</summary>
    public bool Overwrite { get; set; }

    public List<string> Messages { get; set; } = new List<string>();
}
