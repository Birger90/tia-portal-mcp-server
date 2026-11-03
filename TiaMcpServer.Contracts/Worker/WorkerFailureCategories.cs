namespace TiaMcpServer.Contracts.Worker;

/// <summary>
/// Closed vocabulary of failure categories shared by the worker, the wire contract
/// (<see cref="WorkerResponse.FailureCategory"/>), and the host result (the host's
/// <c>WorkerCallResult.FailureCategory</c> references these same constants directly, since
/// <c>TiaMcpServer</c> already depends on this assembly). Every failure must use one of these
/// values; nothing downstream may invent or infer a category from free text.
/// </summary>
public static class WorkerFailureCategories
{
    /// <summary>The caller supplied missing, malformed, or otherwise invalid input.</summary>
    public const string ValidationError = "validation_error";

    /// <summary>The requested project conflicts with what this session/worker is already bound to or has open.</summary>
    public const string BindingConflict = "binding_conflict";

    /// <summary>The Portal connection was lost; the verified project binding is no longer usable.</summary>
    public const string PortalLost = "portal_lost";

    /// <summary>The object or project state no longer matches what the caller last read (for example a content hash mismatch).</summary>
    public const string StateChanged = "state_changed";

    /// <summary>The requested target could not be resolved from the supplied selector.</summary>
    public const string TargetNotFound = "target_not_found";

    /// <summary>The supplied selector resolved to more than one target.</summary>
    public const string TargetAmbiguous = "target_ambiguous";

    /// <summary>The supplied target evidence no longer matches the resolved target.</summary>
    public const string TargetEvidenceMismatch = "target_evidence_mismatch";

    /// <summary>The selected target kind is not supported by the requested operation.</summary>
    public const string TargetKindUnsupported = "target_kind_unsupported";

    /// <summary>The active project container does not deliver the requested operation.</summary>
    public const string UnsupportedCapability = "unsupported_capability";

    /// <summary>The worker responded, but the operation itself failed for a reason not covered by another category.</summary>
    public const string WorkerOperationFailed = "worker_operation_failed";

    /// <summary>
    /// TIA Portal itself rejected a mutation (an <c>EngineeringTargetInvocationException</c> during a write method).
    /// The attempt reached Siemens, so it is not a precondition refusal; the Siemens text rides on
    /// <see cref="WorkerResponse.EngineeringError"/>. Read methods never use this category.
    /// </summary>
    public const string EngineeringRejected = "engineering_rejected";

    /// <summary>The worker did not respond within the request timeout. The operation outcome is unknown; never retried automatically.</summary>
    public const string WorkerTimeout = "worker_timeout";

    /// <summary>The worker process crashed, the pipe broke, or the response was null/malformed. The operation outcome is unknown; never retried automatically.</summary>
    public const string WorkerCrashed = "worker_crashed";

    /// <summary>An operation reported success but a required postcondition (e.g. a resolved project path) was missing.</summary>
    public const string PostconditionFailed = "postcondition_failed";

    /// <summary>The operation is not permitted in the current access mode (e.g. a write attempted in read-only mode).</summary>
    public const string AccessDenied = "access_denied";

    /// <summary>
    /// The worker reported success but its payload did not match the declared result contract for
    /// the operation — malformed, unknown, incorrectly cased, incorrectly typed, or structurally
    /// invalid. The operation may well have been performed; only the response is untrustworthy.
    /// </summary>
    public const string ProtocolError = "protocol_error";

    /// <summary>The supplied cursor was missing, malformed, or could not be decoded.</summary>
    public const string InvalidCursor = "invalid_cursor";

    /// <summary>The cursor was issued for a different set of list filters.</summary>
    public const string CursorFilterMismatch = "cursor_filter_mismatch";

    /// <summary>The cursor was issued against a different project snapshot.</summary>
    public const string CursorSnapshotMismatch = "cursor_snapshot_mismatch";

    /// <summary>The cursor points beyond the available result range.</summary>
    public const string CursorOutOfRange = "cursor_out_of_range";

    /// <summary>The cursor was issued for a different verified worker/project binding.</summary>
    public const string CursorBindingMismatch = "cursor_binding_mismatch";

    /// <summary>The typed project-tree selector is structurally invalid or cannot describe a tree transition.</summary>
    public const string InvalidSelector = "invalid_selector";

    /// <summary>The materialized project-tree snapshot exceeds its configured size limit.</summary>
    public const string SnapshotTooLarge = "snapshot_too_large";

    /// <summary>A continuation refers to a project-tree snapshot that is no longer available.</summary>
    public const string SnapshotUnavailable = "snapshot_unavailable";

    /// <summary>A single project-tree result item cannot fit within the response character limit.</summary>
    public const string ResultItemTooLarge = "result_item_too_large";

    /// <summary>Required project-tree result metadata cannot fit within the response character limit.</summary>
    public const string ResultMetadataTooLarge = "result_metadata_too_large";

    /// <summary>A write guard stopped the call: an unacknowledged acknowledge guard or a block guard fired.</summary>
    public const string GuardBlocked = "guard_blocked";

    /// <summary>The S7-PLCSIM Advanced API could not be located or loaded (not installed, wrong directory, or a load failure).</summary>
    public const string PlcSimApiUnavailable = "plcsim_api_unavailable";

    /// <summary>
    /// The loaded S7-PLCSIM Advanced API does not expose a member the operation needs with one of its
    /// declared signatures; the message names the member. Run <c>plcsim_read probe_api</c> for the full surface.
    /// </summary>
    public const string PlcSimApiMemberMissing = "plcsim_api_member_missing";

    /// <summary>
    /// The S7-PLCSIM Advanced API raised <c>SimulationRuntimeException</c> or
    /// <c>SimulationInitializationException</c>; the message carries the exception type, the
    /// <c>ERuntimeErrorCode</c> value when readable, and the API message.
    /// </summary>
    public const string PlcSimRuntimeError = "plcsim_runtime_error";

    /// <summary>
    /// The S7-1500 Web API endpoint of a configured target could not be reached: connection, DNS or TLS
    /// failure (including a rejected server certificate) or the configured per-request timeout.
    /// </summary>
    public const string WebApiUnreachable = "webapi_unreachable";

    /// <summary>
    /// <c>Api.Login</c> failed for a configured target, or the credential environment variables the
    /// configuration names are not set. Credentials are never echoed.
    /// </summary>
    public const string WebApiAuthenticationFailed = "webapi_authentication_failed";

    /// <summary>The PLC refused the call for the logged-in (or anonymous) user: Web API error code 2.</summary>
    public const string WebApiPermissionDenied = "webapi_permission_denied";

    /// <summary>The PLC answered with another Web API error or HTTP status; the message carries its code and text.</summary>
    public const string WebApiRejected = "webapi_rejected";

    /// <summary>
    /// A TIA Openness member an operation binds at runtime (an undocumented or only claimed member, e.g.
    /// <c>PlcMasterSecretConfigurator.Unprotect()</c>, or a Siemens.Engineering.Safety member when the STEP 7 Safety
    /// option is not installed) is absent from the installed V21 API or has another signature; the message names the
    /// member and what was found. Nothing was changed.
    /// </summary>
    public const string ApiMemberMissing = "api_member_missing";

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        ValidationError,
        BindingConflict,
        PortalLost,
        StateChanged,
        TargetNotFound,
        TargetAmbiguous,
        TargetEvidenceMismatch,
        TargetKindUnsupported,
        UnsupportedCapability,
        WorkerOperationFailed,
        EngineeringRejected,
        WorkerTimeout,
        WorkerCrashed,
        PostconditionFailed,
        AccessDenied,
        ProtocolError,
        InvalidCursor,
        CursorFilterMismatch,
        CursorSnapshotMismatch,
        CursorOutOfRange,
        CursorBindingMismatch,
        InvalidSelector,
        SnapshotTooLarge,
        SnapshotUnavailable,
        ResultItemTooLarge,
        ResultMetadataTooLarge,
        GuardBlocked,
        PlcSimApiUnavailable,
        PlcSimApiMemberMissing,
        PlcSimRuntimeError,
        WebApiUnreachable,
        WebApiAuthenticationFailed,
        WebApiPermissionDenied,
        WebApiRejected,
        ApiMemberMissing
    };

    private static readonly HashSet<string> PreconditionRefusalSet = new(StringComparer.Ordinal)
    {
        ValidationError,
        TargetNotFound,
        TargetAmbiguous,
        StateChanged,
        GuardBlocked,
        BindingConflict,
        AccessDenied
    };

    /// <summary>Categories normally used for precondition refusals; a returned outcome may still describe partial mutation.</summary>
    public static IReadOnlyCollection<string> PreconditionRefusals => PreconditionRefusalSet;

    /// <summary>True when <paramref name="category"/> normally denotes a precondition refusal (see <see cref="PreconditionRefusals"/>).</summary>
    public static bool IsPreconditionRefusal(string? category) => category is not null && PreconditionRefusalSet.Contains(category);

    /// <summary>True when <paramref name="value"/> is exactly one of the approved category constants.</summary>
    public static bool IsKnown(string? value) => value is not null && Known.Contains(value);
}
