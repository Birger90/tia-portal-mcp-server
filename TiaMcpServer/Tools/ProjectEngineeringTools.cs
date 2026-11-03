using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Tools;

/// <summary>Project engineering actions exposed in read-write and full modes.</summary>
[McpServerToolType]
public class ProjectEngineeringTools
{
    [McpServerTool(Name = "compile_check", ReadOnly = false, Destructive = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(CompileCheckResponse))]
    [Description("Compile the bound project and return compiler messages. scope software (default) compiles the PLC program only (PlcSoftware, or one block with blockPath) and does NOT compile the hardware configuration, so hardware errors a download reports (e.g. an access level or confidential-data protection that needs a password, certificates) stay invisible there; scope hardware compiles the hardware configuration of each PLC's CPU device item (its ICompilable service; report scope hardware). Run both before plc_download. Messages are only rows with a description: TIA Portal's container rows (empty or blank text, e.g. the PLC, Hardware configuration, a rack) are not listed, and each message's path is the chain of its containers' paths and its own joined with '/' (e.g. PLC_F1/Hardware configuration/Profilschiene_0; display context, not a selector); errorCount and warningCount are TIA Portal's totals. Available in read-write and full modes.")]
    public static async Task<CallToolResult> CompileCheck(
        OpennessWorkerClient workerClient,
        [Description("Optional PLC software or device name to compile; omitted compiles every PLC.")] string? plcName = null,
        [Description("Optional PLC block path to compile only that block (scope software only).")] string? blockPath = null,
        [Description("Optional compile scope: software (default; PLC program only) or hardware (hardware configuration of the CPU device item; not combinable with blockPath).")] string? scope = null)
    {
        var accessDenial = workerClient.AccessPolicy.Authorize("compile_check");
        if (accessDenial is not null)
            return StructuredStandaloneResult.Create<CompileCheckReport>("compile_check", null,
                accessDenial.Warnings, StandalonePayloadContract.Failure(accessDenial));

        if (CompileScopes.Error(scope, blockPath) is { } scopeError)
            return StructuredStandaloneResult.Create<CompileCheckReport>("compile_check", null,
                Array.Empty<string>(), new(WorkerFailureCategories.ValidationError, scopeError));

        var bindingGate = await workerClient.RequireVerifiedBindingAsync().ConfigureAwait(false);
        if (!bindingGate.Success)
        {
            return StructuredStandaloneResult.Create<CompileCheckReport>("compile_check", null,
                bindingGate.Warnings, StandalonePayloadContract.Failure(bindingGate));
        }

        var execution = await workerClient.ExecuteWithPinnedBindingAsync(
            workerClient.BindingSnapshot,
            () => workerClient.CompileCheckAsync(blockPath: blockPath, plcName: plcName, scope: scope)).ConfigureAwait(false);
        var result = execution.Success ? execution.Value! : execution.Failure!;
        var rejection = StandalonePayloadContract.Rejection(result);
        return StructuredStandaloneResult.Create("compile_check",
            rejection is null ? StandalonePayloadContract.DecodeCompile(result) : null,
            result.Warnings, rejection);
    }
}
