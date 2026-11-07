using System.ComponentModel;

namespace TiaMcpServer.Plc;

/// <summary>The field of the operation reference (describe_operations, K91).</summary>
public sealed partial class PlcOperationRequest
{
    [Description("describe_operations: 1 to 3 PLC read or write operation names to describe in full (several items per call are fine); omitted, the index of every operation.")]
    public IReadOnlyList<string>? OperationNames { get; set; }
}
