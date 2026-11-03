using System.Text;
using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.OpennessWorker.Worker;
using TiaMcpServer.PlcSimWorker.Operations;
using TiaMcpServer.PlcSimWorker.Runtime;

namespace TiaMcpServer.PlcSimWorker;

/// <summary>
/// The S7-PLCSIM Advanced worker process: newline-delimited JSON requests on stdin, one response
/// line per request on stdout, diagnostics on stderr. It is started lazily by the host on the first
/// PLCSIM tool call, independently of the Openness worker and of any project binding.
/// </summary>
internal static class Program
{
    private static int Main()
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        Console.InputEncoding = utf8;
        Console.OutputEncoding = utf8;

        // The protocol owns the real stdout. Anything else (including the Siemens API) that writes
        // to Console.Out is redirected to stderr so it can never desynchronize the response stream.
        var protocolOut = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = false };
        Console.SetOut(Console.Error);

        var mode = WorkerOperationAuthorization.ParseAccessMode(Environment.GetCommandLineArgs());
        Console.Error.WriteLine($"PLCSIM Advanced worker access mode: {McpAccessModeNames.ToName(mode).ToUpperInvariant()}");

        var runtime = new ReflectionPlcSimRuntime(PlcSimMachineEnvironment.Locate, PlcSimApiLoader.Load);
        var dispatcher = new PlcSimWorkerDispatcher(mode, new PlcSimOperationHandler(runtime, new SystemPlcSimClock()));
        using var input = new StreamReader(Console.OpenStandardInput(), utf8);
        string? line;
        while ((line = input.ReadLine()) is not null)
        {
            var response = dispatcher.HandleLine(line);
            protocolOut.WriteLine(JsonSerializer.Serialize(response, WorkerJson.Envelope));
            protocolOut.Flush();
        }

        return 0;
    }
}
