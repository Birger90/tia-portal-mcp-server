using System.Text.Json.Serialization;
using TiaMcpServer.Contracts.Project;

namespace TiaMcpServer.ProjectLifecycle;

/// <summary>Fresh source evidence and the requested destination and consequences.</summary>
public sealed record LifecycleEffects(
    string? SourceProjectPath,
    string? DestinationProjectPath,
    string? DestinationDirectory,
    ProjectStatusInfo? SourceStatus,
    bool? SourceOpenedByWorker,
    bool WillCloseSource,
    bool SavesSource,
    bool RebindsToDestination,
    bool TargetExists,
    string? ArchiveMode,
    string? ArchivePath,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ProjectContextInfo? SourceContext = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] LifecycleOpenProjectCheck? OpenProject = null)
{
    internal LifecycleEffects ForConsequenceComparison()
    {
        static ProjectContextInfo? Stable(ProjectContextInfo? source)
        {
            if (source is null) return null;
            var copy = source.DeepCopy();
            copy.ConnectionObservation = null;
            copy.Capabilities = new();
            return copy;
        }

        var status = SourceStatus is null ? null : new ProjectStatusInfo
        {
            IsOpen = SourceStatus.IsOpen,
            Path = SourceStatus.Path,
            Name = SourceStatus.Name,
            Author = SourceStatus.Author,
            IsModified = SourceStatus.IsModified,
            Version = SourceStatus.Version,
            Size = SourceStatus.Size,
            CreationTime = SourceStatus.CreationTime,
            LastModified = SourceStatus.LastModified,
            LastModifiedBy = SourceStatus.LastModifiedBy,
            Metadata = SourceStatus.Metadata,
            Context = Stable(SourceStatus.Context)
        };
        return this with { SourceContext = Stable(SourceContext), SourceStatus = status };
    }
}

/// <summary>
/// create_project only (K16): whether a project is open in the TIA Portal the new project would be created in.
/// <see cref="Source"/> is <c>binding</c> (the verified bound project, read by the lifecycle probe), <c>portalInventory</c>
/// (the non-attaching Portal listing of an already started worker: the attached Portal, else the only running one) or
/// <c>notChecked</c> (<see cref="Note"/> says why). <see cref="OpenProjectPath"/> is null when no open project was observed.
/// </summary>
public sealed record LifecycleOpenProjectCheck(string Source, int? PortalProcessId, string? OpenProjectPath, bool? IsModified, string? Note)
{
    public const string Binding = "binding";
    public const string PortalInventory = "portalInventory";
    public const string NotChecked = "notChecked";
}
