using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Plc;

/// <summary>
/// The technology object guards: informative only, so nothing asks the user (access mode is the control). The shared
/// <c>plc_state_unverifiable</c> block guard still applies to technology object items (incomplete inventory, unreadable
/// planning reads).
/// </summary>
public static partial class PlcGuardDefinitions
{
    public const string DeletesTechnologyObject = "plc_deletes_technology_object";
    public const string DeletesTechnologyObjectGroupContents = "plc_deletes_technology_object_group_contents";
    public const string TechnologyObjectImportReplaces = "plc_technology_object_import_replaces";
    public const string OverwritesFile = "plc_overwrites_file";
    public const string TechnologyParameterNotHardwareConnection = "plc_technology_parameter_not_hardware_connection";
    public const string ReplacesHardwareConnection = "plc_replaces_hardware_connection";

    private static IEnumerable<WriteGuardDefinition> TechnologyObjectDefinitions() => new[]
    {
        new WriteGuardDefinition(DeletesTechnologyObject, WriteGuardSeverities.Info, "Deleting the technology object removes it from the PLC; this operation changes no block, so compile the PLC afterwards to find blocks that still use it."),
        new WriteGuardDefinition(DeletesTechnologyObjectGroupContents, WriteGuardSeverities.Info, "Deleting the technology object group removes every technology object and group inside it."),
        new WriteGuardDefinition(TechnologyObjectImportReplaces, WriteGuardSeverities.Info, "The SimaticML import uses ImportOptions.Override, which is expected to replace an object of the same name in the target group (not verified live; the read-back reports what the group contains afterwards)."),
        new WriteGuardDefinition(OverwritesFile, WriteGuardSeverities.Info, "The export replaces an existing file (overwrite:true)."),
        new WriteGuardDefinition(TechnologyParameterNotHardwareConnection, WriteGuardSeverities.Info, "The Siemens technology-objects skill states that writing Actor.* parameters succeeds without connecting the drive; a hardware connection is made with connect_technology_object_hardware."),
        new WriteGuardDefinition(ReplacesHardwareConnection, WriteGuardSeverities.Info, "The interface reads as connected; Connect is expected to replace its current connection (not documented; the read-back checks that the requested target is connected)."),
    };
}
