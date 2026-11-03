namespace TiaMcpServer.Contracts.Plc;

/// <summary>
/// Worker method names of the PLC download feature. They feed <c>OperationPolicyCatalog</c>,
/// <c>ProjectCapabilityCatalog</c> and the worker dispatch switch, so the three cannot drift.
/// </summary>
public static class PlcDownloadMethods
{
    /// <summary>The guarded online download (<c>OnlineControl</c>); also the public tool name.</summary>
    public const string Download = "plc_download";

    /// <summary>The offline download-path listing (<c>Observe</c>); a <c>plc_read</c> operation.</summary>
    public const string ListTargets = "list_download_targets";
}
