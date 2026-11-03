namespace TiaMcpServer.Contracts.PlcSim;

/// <summary>
/// Exact host/PLCSIM-worker handshake. The PLCSIM worker reuses the Openness wire envelope
/// (<c>WorkerRequest</c>/<c>WorkerResponse</c>, newline-delimited JSON, a <c>hello</c> request before
/// the first real request) but is a different protocol, so it advertises its own version and
/// capability set and never the Openness ones.
/// </summary>
public static class PlcSimWorkerProtocol
{
    public const string Version = "plcsim-v1";

    public static readonly string[] RequiredCapabilities =
    {
        "plcsim-reflection-api-surface-v1",
        "plcsim-exact-instance-name-v1",
        "plcsim-test-runner-v1",
        // WorkerResponse.WorkerRestartRequired is set after a cached API type-initializer failure; a
        // worker build without it would never be recycled, so the host refuses it at the handshake.
        "plcsim-restart-flag-v1"
    };

    /// <summary>Process display name used in host diagnostics.</summary>
    public const string DisplayName = "PLCSIM Advanced worker";
}
