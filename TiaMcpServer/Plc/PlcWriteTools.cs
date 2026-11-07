using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Plc;

[McpServerToolType]
public class PlcWriteTools
{
    [McpServerTool(Name = "plc_write", ReadOnly = false, Destructive = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(PlcGuardedWriteResponse))]
    [Description(PlcToolDescriptions.Write)]
    public static Task<CallToolResult> PlcWrite(
        OpennessWorkerClient workerClient,
        WriteExecution execution,
        [Description(PlcToolDescriptions.WriteOperationsParameter)] PlcOperationRequest[] operations,
        [Description("True previews effects and guards without mutation; omitted or false executes.")] bool dryRun = false,
        McpServer? server = null,
        CancellationToken cancellationToken = default)
        => execution.RunAsync(new PlcWriteDomain(workerClient),
            new WriteCall<PlcOperationRequest>(WithMappedSafetyPasswords(operations ?? [], workerClient.BindingSnapshot.ProjectPath), dryRun),
            // Only calls with fail-safe items ask (read-write); every other plc_write call runs without a prompt.
            new WriteConfirmationContext(server is null ? null : UserConfirmation.For(server, TimeSpan.FromMinutes(2))),
            bindingStrategy: null, cancellationToken);

    /// <summary>
    /// A fail-safe password item without passwordVariable takes the source (a name, never a value) the server's password map
    /// gives for its PLC and purpose <c>safety</c>; without one the catalog refuses the missing field as before.
    /// </summary>
    internal static PlcOperationRequest[] WithMappedSafetyPasswords(PlcOperationRequest[] operations, string? projectPath)
    {
        var project = PlcDownload.PasswordMapStore.ProjectName(projectPath);
        string? Lookup(PlcOperationRequest operation, string purpose)
            => Contracts.Shared.PasswordMapFile.Lookup(PlcDownload.PasswordMapStore.Entries, project, operation.PlcName, purpose);
        foreach (var operation in operations)
        {
            if (operation is null) continue;
            if (operation.PasswordVariable is null && Contracts.Plc.Safety.PlcSafetyMethods.TakesPassword(operation.Operation))
                operation.PasswordVariable = Lookup(operation, Contracts.Shared.PasswordMapFile.SafetyPurpose);
            WithMappedCpuProtectionPasswords(operation, Lookup);
            WithMappedBlockProtectionPassword(operation, project);
        }

        return operations;
    }

    /// <summary>
    /// A block protection write without passwordVariable takes the source the server's password map names for the block's PLC
    /// (from blockPath) and purpose KnowHowProtection (the know-how writes) or BlockWriteProtection (the write-protection writes;
    /// for change_block_write_protection_password the current password). newPasswordVariable is never taken from the map.
    /// </summary>
    internal static void WithMappedBlockProtectionPassword(PlcOperationRequest operation, string? project)
    {
        if (operation.PasswordVariable is not null || !Contracts.Plc.BlockProtection.PlcBlockProtectionMethods.IsWrite(operation.Operation)
            || string.IsNullOrWhiteSpace(operation.BlockPath))
            return;
        string? plc;
        try
        {
            plc = Contracts.Block.BlockAddress.Parse(operation.BlockPath).PlcName;
        }
        catch (ArgumentException)
        {
            // The catalog refuses the path; nothing is looked up.
            return;
        }

        operation.PasswordVariable = Contracts.Shared.PasswordMapFile.Lookup(PlcDownload.PasswordMapStore.Entries, project, plc,
            Contracts.Plc.BlockProtection.PlcBlockProtectionMethods.Purpose(operation.Operation));
    }

    /// <summary>
    /// The CPU protection items take what the server's password map names (sources, never values) for what the call
    /// omits: the confidential-data password (purpose PlcMasterSecretPassword) of protect, change (current) and disable
    /// (without allConfiguration), and for set_plc_access_level every level more open than the requested one (purpose =
    /// the level name).
    /// </summary>
    internal static void WithMappedCpuProtectionPasswords(PlcOperationRequest operation, Func<PlcOperationRequest, string, string?> lookup)
    {
        var security = Contracts.Plc.Security.PlcSecurityPolicy.MasterSecretPurpose;
        switch (operation.Operation)
        {
            case Contracts.Plc.Security.PlcSecurityMethods.ProtectConfidentialData:
            case Contracts.Plc.Security.PlcSecurityMethods.ChangeConfidentialDataPassword:
            case Contracts.Plc.Security.PlcSecurityMethods.DisableConfidentialDataProtection when operation.AllConfiguration != true:
                operation.PasswordVariable ??= lookup(operation, security);
                break;
            case Contracts.Plc.Security.PlcSecurityMethods.SetAccessLevel when operation.AccessLevel is not null
                && Contracts.Plc.Security.PlcSecurityPolicy.AccessLevelError(operation.AccessLevel) is null:
            {
                var given = operation.AccessLevelPasswords?.Entries ?? Array.Empty<Contracts.Plc.PlcDownloadPasswordInfo>();
                var resets = operation.ResetAccessLevelPasswords ?? Array.Empty<string>();
                var added = Contracts.Plc.Security.PlcSecurityPolicy.LevelsAbove(operation.AccessLevel)
                    .Where(level => given.All(e => e.Type != level) && !resets.Contains(level, StringComparer.Ordinal))
                    .Select(level => (Level: level, Source: lookup(operation, level)))
                    .Where(entry => entry.Source is not null)
                    .Select(entry => new Contracts.Plc.PlcDownloadPasswordInfo { Type = entry.Level, Variable = entry.Source! })
                    .ToList();
                if (added.Count > 0) operation.AccessLevelPasswords = new PlcDownload.PlcDownloadPasswordMap(given.Concat(added).ToList());
                break;
            }
        }
    }
}
