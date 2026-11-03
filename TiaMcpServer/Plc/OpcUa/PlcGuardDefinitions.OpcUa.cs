using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Plc;

/// <summary>The OPC UA guards (issue #24): informative only, the access mode is the control.</summary>
public static partial class PlcGuardDefinitions
{
    public const string OpcUaChangesServer = "plc_opcua_changes_server";
    public const string OpcUaLowersAccess = "plc_opcua_lowers_access";

    private static IEnumerable<WriteGuardDefinition> OpcUaDefinitions() => new[]
    {
        new WriteGuardDefinition(OpcUaChangesServer, WriteGuardSeverities.Info,
            "The operation changes the PLC's OPC UA server configuration in the offline project (an interface, a namespace, a role); OPC UA clients see it after the next download."),
        new WriteGuardDefinition(OpcUaLowersAccess, WriteGuardSeverities.Info,
            "The operation relaxes OPC UA access rules (a namespace restriction no longer requires a session, signing or encryption, browse is no longer restricted or a permission is granted); clients get more access after the next download."),
    };
}
