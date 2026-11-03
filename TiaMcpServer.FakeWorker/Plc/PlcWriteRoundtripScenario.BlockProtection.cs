using Siemens.Engineering.SW.Blocks;
using TiaMcpServer.Contracts.Plc.BlockProtection;
using TiaMcpServer.Contracts.Plc.Safety;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.FakeWorker.Plc;

/// <summary>
/// Block know-how and write protection (issue #5) against stand-in providers the production <see cref="PlcBlockProtectionRun"/>
/// binds and calls exactly like the worker. Every PLC_1 block lists both providers except <c>Locked</c> (none); PLC_2's blocks list
/// only the know-how provider. <c>Protected_FC</c> starts know-how protected with the password of the variable
/// <c>TIA_MCP_TEST_KNOW_HOW</c> (when set). Fail-safe blocks follow the scenario's safety login rule.
/// </summary>
sealed partial class PlcWriteRoundtripScenario
{
    private readonly Dictionary<string, FakeBlockProtectionState> _protection = new(Names);

    private string BlockProtection(WorkerRequest request)
    {
        var info = request.PlcBlockProtection ?? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "missing request");
        var (plc, group, block, path) = ResolveBlock(info.BlockPath);
        var language = group.Blocks.First(b => Names.Equals(b.Name, block)).Language;
        var state = ProtectionState(plc.Software, block);
        var offered = Names.Equals(block, "Locked") ? new List<Type>()
            : plc.Software == "PLC_1" ? new List<Type> { typeof(PlcBlockProtectionProvider), typeof(PlcBlockWriteProtectionProvider) }
            : new List<Type> { typeof(PlcBlockProtectionProvider) };
        var knowHow = new PlcBlockProtectionProvider(state, PortalRejection);
        var write = new PlcBlockWriteProtectionProvider(state, PortalRejection);
        var target = new PlcBlockProtectionTarget(plc.Software, path, block, language, new FakeProtectableBlock(state), offered,
            type => type == typeof(PlcBlockProtectionProvider) ? knowHow : type == typeof(PlcBlockWriteProtectionProvider) ? write : null);
        try
        {
            // Like its export, the read of "Locked" fails (access denied): the planning read is unreadable (security review F7 tests).
            if (request.Method == PlcBlockProtectionMethods.GetBlockProtection && Names.Equals(block, "Locked"))
                throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, "The block protection could not be read: access denied.");
            if (request.Method == PlcBlockProtectionMethods.GetBlockProtection) return Ok(PlcBlockProtectionRun.Read(target, _ => false));
            if (PlcSafetyPolicy.IsFailSafeLanguage(language)) RequireFailSafeImportPermitted(plc, block, null, request.Method);
            return Ok(PlcBlockProtectionRun.Write(request.Method, target, info, _ => false,
                PlcBlockProtectionRun.Resolver(Environment.GetEnvironmentVariable, null)));
        }
        catch (RuntimeBindingException exception)
        {
            throw new WorkerOperationException(exception.Category, exception.Message);
        }
    }

    private FakeBlockProtectionState ProtectionState(string plc, string block)
    {
        var key = plc + "/" + block;
        if (_protection.TryGetValue(key, out var state)) return state;
        state = new FakeBlockProtectionState();
        if (Names.Equals(block, "Protected_FC") && Environment.GetEnvironmentVariable("TIA_MCP_TEST_KNOW_HOW") is { Length: > 0 } password)
            state.KnowHowPassword = password;
        _protection[key] = state;
        return state;
    }
}
