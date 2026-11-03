using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Security;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.FakeWorker.Plc;

/// <summary>
/// Stateful <c>plc-security</c> fixture: PLC_1 (Station_1) whose CPU lists the three protection services as stand-in types
/// and runs the worker's own Siemens-free <see cref="PlcSecurityRun"/> against them. No password is configured, so reading
/// the access level throws NullReferenceException before and after a change, as the Siemens security skill documents.
/// <c>plc-security-no-unprotect</c> offers a configurator without <c>void Unprotect()</c>. <c>plc-security-control-unavailable</c>
/// lists the access-control provider but returns null for it, as GetService did live on V21 on 2026-11-02 (D14). This
/// models member shapes only; it is no evidence of Siemens behavior.
/// </summary>
sealed class PlcSecurityScenario(bool withUnprotect, bool controlRetrievable = true)
{
    private readonly Siemens.Engineering.HW.Features.PlcAccessLevelProvider _level = new();
    private readonly Siemens.Engineering.HW.Features.PlcAccessControlConfigurationProvider _control = new();
    private readonly Siemens.Engineering.HW.Features.PlcMasterSecretConfigurator _secret = new();
    private readonly Siemens.Engineering.Umac.PlcMasterSecretConfigurator _secretWithoutUnprotect = new();

    private PlcSecurityCpu Cpu()
    {
        var offered = new List<Type> { _level.GetType(), _control.GetType(), withUnprotect ? _secret.GetType() : _secretWithoutUnprotect.GetType() };
        return new PlcSecurityCpu("PLC_1", "Station_1", offered, type =>
            type == _level.GetType() ? _level : type == _control.GetType() ? (controlRetrievable ? _control : null) : type == _secret.GetType() ? _secret : _secretWithoutUnprotect);
    }

    public string Handle(string line)
    {
        var request = JsonSerializer.Deserialize<WorkerRequest>(line, WorkerJson.Envelope)!;
        try
        {
            if (request.Method == "list_tag_tables")
                return Ok(WorkerJson.SerializePayload(new PlcTagInventoryInfo
                {
                    Plcs = { new PlcTagInventoryPlcInfo { PlcName = "PLC_1", DeviceName = "Station_1" } },
                }), null);
            var info = request.PlcSecurity ?? throw new PlcSecurityBindingException(WorkerFailureCategories.ValidationError, $"{request.Method} requires the CPU protection request.");
            if (info.PlcName is not null && !Same(info.PlcName, "PLC_1") && !Same(info.PlcName, "Station_1"))
                throw new PlcSecurityBindingException(WorkerFailureCategories.TargetNotFound, $"No PLC software named '{info.PlcName}' was found in the project.");
            if (request.Method == PlcSecurityMethods.GetSecurity)
                return Ok(WorkerJson.SerializePayload(new PlcSecurityReadInfo { Plcs = { PlcSecurityRun.Read(Cpu(), _ => false) } }), null);
            if (!PlcSecurityMethods.IsWrite(request.Method))
                throw new PlcSecurityBindingException(WorkerFailureCategories.ValidationError, $"plc-security scenario does not script '{request.Method}'.");
            var (result, warnings) = PlcSecurityRun.Write(request.Method, Cpu(), info, _ => false);
            return Ok(WorkerJson.SerializePayload(result), warnings);
        }
        catch (PlcSecurityBindingException ex)
        {
            return JsonSerializer.Serialize(new WorkerResponse { Success = false, FailureCategory = ex.Category, Error = ex.Message }, WorkerJson.Envelope);
        }
    }

    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string Ok(string payload, List<string>? warnings)
        => JsonSerializer.Serialize(new WorkerResponse { Success = true, Payload = payload, Warnings = warnings is { Count: > 0 } ? warnings : null }, WorkerJson.Envelope);
}
