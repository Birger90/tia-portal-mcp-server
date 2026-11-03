namespace TiaMcpServer.Contracts.Safety;

/// <summary>
/// Classifies every worker operation by its intent. Both the host and worker use this
/// shared classification to enforce access policy. An operation that has no explicit
/// classification is denied in every mode (deny-by-default).
/// </summary>
public enum OperationCapability
{
    /// <summary>Read-only observation: inspect project data without side effects.</summary>
    Observe,

    /// <summary>Creates temporary files internally (e.g. block export) but does not persist
    /// output. Allowed in read-only mode when cleanup is guaranteed.</summary>
    TemporaryExport,

    /// <summary>Invokes the Siemens compilation API. Not allowed in read-only mode because
    /// compilation may modify internal project state.</summary>
    Compile,

    /// <summary>Opens, creates, saves, archives, or closes a project.</summary>
    ProjectLifecycle,

    /// <summary>Modifies project data: blocks, tags, tag tables, user constants, network devices.</summary>
    ProjectMutation,

    /// <summary>
    /// Changes a target device or TIA Portal's online connection to it: <c>plc_download</c>, which
    /// downloads to PLCSIM or to real hardware, the S7-1500 Web API control methods of
    /// <c>plc_online_write</c> (<c>webapi_*</c>, real controllers configured by the user) and the
    /// Openness go-online/go-offline methods of <c>plc_tia_online</c>. Allowed in read-write and
    /// full, never in read-only: the access mode is the human's control over these changes.
    /// </summary>
    OnlineControl,

    /// <summary>Selects an already-open Portal project without opening, saving, or closing it.</summary>
    SessionSelection,

    /// <summary>
    /// Controls an S7-PLCSIM Advanced virtual CPU through the separate PLCSIM worker: power, run/stop,
    /// memory reset, tag-list refresh, tag writes, scale factor and test scenarios. It never touches a
    /// TIA Portal project. Appended last so existing numeric values stay stable.
    /// </summary>
    SimulationControl,

    /// <summary>
    /// Connects to a target device but changes neither the target nor the project: <c>compare_to_online</c>
    /// (<c>PlcSoftware.CompareToOnline</c>). Allowed in every mode because read-only means observation; kept
    /// apart from <see cref="Observe"/> so the network reach is explicit and can be tightened without
    /// touching offline reads. Appended last so existing numeric values stay stable.
    /// </summary>
    OnlineObserve
}
