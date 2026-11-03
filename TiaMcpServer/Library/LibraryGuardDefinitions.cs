using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Library;

/// <summary>
/// library_write guards. All are informative (owner decision: the access mode is the control; only invalid or
/// ambiguous input refuses; and 2026-11-01: never stricter than TIA Portal), and none needs acknowledgement.
/// <see cref="CloseUnsavedChanges"/> names a close_global_library that discards unsaved changes this server made: the
/// documented <c>Close()</c> offers no save-or-discard choice (see <see cref="LibraryUnsavedChanges"/>), TIA Portal lets
/// a user close without saving, and a close is a lifecycle item, so read-write confirms the call and the prompt says so.
/// </summary>
public static class LibraryGuardDefinitions
{
    public const string WritesFiles = "library_writes_files";
    public const string UpgradesInPlace = "library_upgrades_in_place";
    public const string AlreadyOpen = "library_already_open";
    public const string CloseUnsaved = "library_close_unsaved";
    public const string CloseUnsavedChanges = "library_close_unsaved_changes";
    public const string Deletes = "library_deletes";
    public const string ReplacesObjects = "library_replaces_objects";
    public const string SyncsProjectLibrary = "library_syncs_project_library";
    public const string UpdateDefaults = "library_update_defaults";
    public const string GlobalUnsaved = "library_global_unsaved";
    public const string ChangesProject = "library_changes_project";
    public const string SecondaryProject = "library_secondary_project";
    public const string PortalSetting = "library_portal_setting";
    public const string PortalSecuritySetting = "library_portal_security_setting";
    public const string ExportOverwritesFiles = "library_export_overwrites_files";

    /// <summary>
    /// The info guards (<see cref="WriteGuardCatalog.NonInteractive"/>) plus the one acknowledge guard of issue #27: a change of a
    /// security-relevant TIA Portal setting (certificates, communication, encryption, passwords, trust, acceptance, access by the names
    /// the worker resolved) is acknowledged in read-write (every set_portal_setting call confirms) and satisfied by policy in full.
    /// </summary>
    public static IReadOnlyList<WriteGuardDefinition> Definitions { get; } = Array.AsReadOnly(InfoDefinitions.Append(
        new WriteGuardDefinition(PortalSecuritySetting, WriteGuardSeverities.Acknowledge,
            "The item changes a security-relevant TIA Portal setting (the folder or setting name TIA Portal resolved mentions security, certificates, communication, encryption, passwords, trust, acceptance, allowing, legacy, TLS or access); a change can weaken how TIA Portal protects connections or projects for this whole TIA Portal instance. In read-write the confirmation covers it; full satisfies it by policy.")).ToArray());

    private static IReadOnlyList<WriteGuardDefinition> InfoDefinitions => WriteGuardCatalog.NonInteractive(new[]
    {
        new WriteGuardDefinition(PortalSetting, WriteGuardSeverities.Info, "The item changes a TIA Portal setting; it applies to this TIA Portal instance, not to the project, and is not saved with the project. Read-write confirms every such call."),
        new WriteGuardDefinition(WritesFiles, WriteGuardSeverities.Info, "The item creates, extracts, opens read-write or overwrites global library files outside the project."),
        new WriteGuardDefinition(UpgradesInPlace, WriteGuardSeverities.Info, "OpenWithUpgrade may upgrade the library file in place on disk."),
        new WriteGuardDefinition(AlreadyOpen, WriteGuardSeverities.Info, "The library file is already open; nothing is called and its open mode is not changed."),
        new WriteGuardDefinition(CloseUnsaved, WriteGuardSeverities.Info, "Close() takes no save-or-discard argument and no documented member reports unsaved changes; changes made outside this server are not known, and a TIA Portal confirmation raised by Close() is answered by portalConfirmations and recorded."),
        new WriteGuardDefinition(CloseUnsavedChanges, WriteGuardSeverities.Info, "The global library has changes this server made and did not save; the close discards them (save_global_library first to keep them)."),
        new WriteGuardDefinition(Deletes, WriteGuardSeverities.Info, "The item deletes a master copy, a type, a type version, a folder with its contents or unused library content, or discards an in-work version."),
        new WriteGuardDefinition(ReplacesObjects, WriteGuardSeverities.Info, "MasterCopyMode Replace replaces existing objects of the same names in the target."),
        new WriteGuardDefinition(SyncsProjectLibrary, WriteGuardSeverities.Info, "A global-library type version is synchronized into the project library before its instance is created."),
        new WriteGuardDefinition(UpdateDefaults, WriteGuardSeverities.Info, "UpdateProject or UpdateLibrary without modes applies TIA Portal's default force-update, unused-version and structure-conflict behavior."),
        new WriteGuardDefinition(SecondaryProject, WriteGuardSeverities.Info, "The item opens a read-only secondary (reference) project next to the bound project; the binding does not change and content tools stay on the bound project. Close it with close_secondary_project."),
        new WriteGuardDefinition(ChangesProject, WriteGuardSeverities.Info, "HarmonizeProject renames or moves project objects to match the library's names or paths; compile afterwards."),
        new WriteGuardDefinition(ExportOverwritesFiles, WriteGuardSeverities.Info, "The export writes files at a caller path; with overwrite:true existing files are replaced."),
        new WriteGuardDefinition(GlobalUnsaved, WriteGuardSeverities.Info, "Changes to a global library persist only through save_global_library."),
    }, "Library");
}
