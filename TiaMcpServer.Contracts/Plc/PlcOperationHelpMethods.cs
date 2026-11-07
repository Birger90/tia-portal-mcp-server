namespace TiaMcpServer.Contracts.Plc;

/// <summary>
/// The operation reference of <c>plc_read</c>: <c>describe_operations</c> returns the required and optional fields, the
/// full field documentation and the reference text of PLC read and write operations. It is computed by the host from the
/// operation catalog and the tool reference texts, reads nothing from TIA Portal and needs no bound project, so it is
/// <c>Observe</c> and has no worker method (K91: the full texts made the plc_read/plc_write tool listing too large for
/// Claude Desktop).
/// </summary>
public static class PlcOperationHelpMethods
{
    public const string DescribeOperations = "describe_operations";
}
