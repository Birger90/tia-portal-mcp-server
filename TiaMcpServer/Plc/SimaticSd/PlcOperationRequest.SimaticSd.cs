using System.ComponentModel;

namespace TiaMcpServer.Plc;

/// <summary>The fields of the SIMATIC SD network operations (insert_network, replace_network).</summary>
public sealed partial class PlcOperationRequest
{
    [Description("insert_network (required), replace_network (required): the code of exactly one network in SIMATIC SD (.s7dcl) text: a RUNG body (LAD RUNG wire#powerrail ... END_RUNG, FBD RUNG \"Tag\" ... END_RUNG, several RUNGs allowed), a full NETWORK ... END_NETWORK (with or without the { S7_Language := \"...\" } pragma before it, single- or multi-line), or SCL statements. As the Siemens V21 simatic-sd skill prescribes, only the outer wrappers and the S7_Language pragma are stripped; inner pragmas such as { S7_Templates := \"time_type := Time\" } stay; blank lines are dropped. Do not wrap the code in quotes. A second NETWORK, unbalanced RUNG/END_RUNG or NETWORK/END_NETWORK and S7_NetworkTitle/S7_NetworkComment pragma keys are refused (titles are .s7res ids: set them with set_network_text).")]
    public string? Code { get; set; }

    [Description("insert_network: the 1-based position the new network takes (it is inserted before the network at that position; one past the last appends). Omitted appends. set_plc_project_language (required): the 1-based row of the CPU's MultilingualSupport table (get_cpu_settings, section multilingualSupport).")]
    public int? Position { get; set; }
}
