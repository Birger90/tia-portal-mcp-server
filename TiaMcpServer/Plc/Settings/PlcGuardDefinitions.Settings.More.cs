using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Plc;

/// <summary>The guards of the settings operations beyond issue #15.</summary>
public static partial class PlcGuardDefinitions
{
    public const string ChangesProjectAccess = "plc_changes_project_access";
    public const string ImportsCertificate = "plc_imports_certificate";
    public const string SafetyPrintoutIsNoAcceptance = "plc_safety_printout_is_no_acceptance";
    public const string ProtectsProject = "plc_protects_project";
    public const string ChangesMarkings = "plc_changes_markings";
    public const string LoadsCamData = "plc_loads_cam_data";

    /// <summary>The settings family's acknowledge guards (read-write confirms the call, full satisfies them by policy).</summary>
    private static IEnumerable<WriteGuardDefinition> SettingsAcknowledgeDefinitions() => new[]
    {
        new WriteGuardDefinition(ProtectsProject, WriteGuardSeverities.Acknowledge,
            "protect_project protects the project with UMAC (ProjectBase.ProtectProject): from then on only users with engineering function rights may open and change it, and this server's writes need the function right \"Modify project via Openness API\". The administrator's password comes from a password source; if that password is lost, the project may no longer be opened. TIA Portal documents no way back to an unprotected project through Openness. Save a copy of the project first."),
        new WriteGuardDefinition(SyslogAutoAcceptsCertificates, WriteGuardSeverities.Acknowledge,
            "set_syslog sets SysLogAutoAcceptClient to true: the CPU then accepts the certificates of syslog clients automatically, without checking them against a trusted certificate. Automatic certificate acceptance needs the human's confirmation; it takes effect on the CPU after a download."),
    };

    private static IEnumerable<WriteGuardDefinition> MoreSettingsDefinitions() => new[]
    {
        new WriteGuardDefinition(ChangesMarkings, WriteGuardSeverities.Info,
            "mark_objects / unmark_objects change which objects of the bound Multiuser local session are marked for check-in (MarkingService). Nothing is checked in, committed or sent to the Project Server; other users see the marks after the session is refreshed or checked in in TIA Portal."),
        new WriteGuardDefinition(LoadsCamData, WriteGuardSeverities.Info,
            "load_cam_data replaces the cam data (the cam profile) of a cam technology object with the content of a file (CamDataSupport.LoadCamData / LoadCamDataBinary). TIA Portal decides whether the file fits; the previous cam data are not kept by this server."),
        new WriteGuardDefinition(SafetyPrintoutIsNoAcceptance, WriteGuardSeverities.Info,
            "generate_safety_printout writes TIA Portal's safety printout (SafetyPrintout.Print) of an F-CPU to a file: a document for the safety acceptance. " + Contracts.Plc.Safety.PlcSafetyPolicy.AcceptanceNotice),
        new WriteGuardDefinition(ImportsCertificate, WriteGuardSeverities.Info,
            "import_cpu_certificate imports a certificate file into the CPU's local certificate store (CertificateComposition.Import). This server trusts nothing: the result reports the certificate's Id and SHA-256 fingerprint for you to compare with the source; the CPU uses it only after it is assigned and downloaded."),
        new WriteGuardDefinition(ChangesProjectAccess, WriteGuardSeverities.Info,
            "The item changes the project's user management and access control (UMAC): roles, project users, engineering or device function rights, UMC users or the anonymous user. Engineering function rights decide who may open and change the project (a protected project needs the function right \"Modify project via Openness API\" for this server's writes, manual \"Modifying project in UMAC\"); device function rights take effect on the device after a download of the user management data. Do not lock yourself out: keep a user with the rights you need."),
    };
}
