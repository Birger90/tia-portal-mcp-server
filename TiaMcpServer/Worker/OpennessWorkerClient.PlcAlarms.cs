using TiaMcpServer.Contracts.PlcAlarms;

namespace TiaMcpServer.Worker;

/// <summary>
/// Worker calls of the PLC alarm, alarm class, text list and supervision operations. Every one runs through the
/// verified binding and capability gates; the writes set the worker-internal confirmation fence like the other writes.
/// </summary>
public partial class OpennessWorkerClient
{
    public Task<WorkerCallResult> PlcAlarmAsync(string method, PlcAlarmRequestInfo alarm)
        => SendBoundProjectRequestAsync(method, null, request =>
        {
            request.PlcAlarm = alarm;
        }, "{}");
}
