using System.Text.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.BlockProtection;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Plc;

/// <summary>The block protection guards (issue #5): informative only, nothing asks the user (fail-safe blocks add the safety guards).</summary>
public static partial class PlcGuardDefinitions
{
    public const string SetsBlockProtection = "plc_sets_block_protection";
    public const string LowersBlockProtection = "plc_lowers_block_protection";

    private static IEnumerable<WriteGuardDefinition> BlockProtectionDefinitions() => new[]
    {
        new WriteGuardDefinition(SetsBlockProtection, WriteGuardSeverities.Info,
            "The operation sets or changes a know-how protection or write protection password of a block in the offline project, from a password source. Keep the source available (environment variable or Windows credential, the server password map): Openness offers no way to read a password back, and a know-how-protected block cannot be opened, exported with its code or changed without it."),
        new WriteGuardDefinition(LowersBlockProtection, WriteGuardSeverities.Info,
            "The operation removes the know-how protection or disables the write protection of a block in the offline project; the block's code is then readable or changeable by everyone with the project."),
    };
}

/// <summary>
/// Planning, payload contracts and verification of the block protection operations (issue #5, not live-tested). The target is
/// the block (PLC matched in the call's inventory); one planning read (<c>get_block_protection</c>, Observe) gives the current
/// state for the effect and tells whether the block is a fail-safe block, whose change follows TIA Portal's safety login rule
/// (the acceptance guard, and the login block guard only when a safety password is set and the session is not logged on). An
/// unreadable planning read informs (<c>plc_state_unverifiable</c>) and the item runs: the worker resolves the block again,
/// re-checks the safety rule and TIA Portal decides. The worker reads the documented state property back after the call; a
/// matched read-back is the item's verification (check <see cref="Check"/>), an unreadable one stays trusted.
/// </summary>
internal static class PlcBlockProtection
{
    public const string Kind = "BlockProtection";

    public const string Check = "blockProtection";

    public static PlcBlockProtectionRequestInfo ToRequest(PlcOperationRequest item) => new()
    {
        BlockPath = item.BlockPath ?? string.Empty,
        PasswordSource = PlcBlockProtectionMethods.IsWrite(item.Operation) ? item.PasswordVariable : null,
        NewPasswordSource = item.Operation == PlcBlockProtectionMethods.ChangeWriteProtectionPassword ? item.NewPasswordVariable : null,
    };

    /// <summary>The setting the item changes: <c>knowHow</c> or <c>writeProtection</c> (one object each per block).</summary>
    public static string Setting(string operation) => PlcBlockProtectionMethods.IsKnowHow(operation) ? "knowHow" : "writeProtection";

    public static async Task<(PlcWriteEffect? Effect, IReadOnlyList<FiredGuard> Guards, WriteToolError? Error)> ResolveAsync(OpennessWorkerClient client,
        PlcOperationRequest item, PlcTagInventoryPlcInfo plc, PlcSafetyPlanning safety)
    {
        var guards = new List<FiredGuard>();
        var read = await client.PlcBlockProtectionAsync(PlcBlockProtectionMethods.GetBlockProtection, ToRequest(item)).ConfigureAwait(false);
        PlcBlockProtectionInfo? current = null;
        if (!read.Success && read.FailureCategory is WorkerFailureCategories.TargetNotFound or WorkerFailureCategories.TargetAmbiguous)
            return (null, guards, new(read.FailureCategory!, $"Operation '{item.OperationId}': {read.Error}"));
        if (read.Success)
        {
            try
            {
                current = CanonicalJson.Deserialize<PlcBlockProtectionInfo>(DecodeRead(item, read.Payload ?? string.Empty).GetRawText());
            }
            catch (JsonException)
            {
                current = null;
            }
        }

        if (current is null)
            guards.Add(new FiredGuard(PlcGuardDefinitions.StateUnverifiable, item.OperationId,
                $"Operation '{item.OperationId}': the protection state of '{item.BlockPath}' could not be read for planning ({(read.Success ? "the result did not decode" : read.Error)}); "
                + "the item runs, the worker resolves the block again and applies TIA Portal's safety login rule to a fail-safe block, and TIA Portal decides."));
        var target = new PlcTargetIdentity(Kind, plc.PlcName, plc.DeviceName, null, null, Setting(item.Operation), current?.BlockPath ?? item.BlockPath, null);
        if (current is null)
        {
            // Security review F7 (fail safe): without the planning read the block may be a fail-safe block. On an F-CPU, or a
            // PLC whose safety administration cannot be read, the acceptance guard fires (read-write confirms); no login block
            // is fired, because the block's fail-safe identity is unknown and TIA Portal decides (the worker gate re-checks).
            var failSafeCpu = await safety.IsFailSafeCpuAsync(plc.PlcName, plc.DeviceName).ConfigureAwait(false);
            if (failSafeCpu != false)
                guards.Add(new FiredGuard(PlcGuardDefinitions.SafetyAcceptance, item.OperationId,
                    $"Operation '{item.OperationId}' ({item.Operation} of '{item.BlockPath}'): the block could not be read for planning and PLC '{plc.PlcName}' "
                    + (failSafeCpu == true ? "is an F-CPU" : "may be an F-CPU (its safety administration could not be read)")
                    + ", so the block may belong to the safety program. " + Contracts.Plc.Safety.PlcSafetyPolicy.AcceptanceNotice));
        }
        else if (current.IsFailSafe)
        {
            var (failSafe, _) = await safety.FailSafeEditAsync(item, target, $"Operation '{item.OperationId}' ({item.Operation} of '{target.BlockPath}')").ConfigureAwait(false);
            guards.AddRange(failSafe);
        }

        guards.Add(new FiredGuard(PlcBlockProtectionMethods.Lowers(item.Operation) ? PlcGuardDefinitions.LowersBlockProtection : PlcGuardDefinitions.SetsBlockProtection,
            item.OperationId, Describe(item, target.BlockPath!)));
        var (field, requested, before) = item.Operation switch
        {
            PlcBlockProtectionMethods.SetKnowHowProtection => ("knowHowProtected", true, current?.State.KnowHowProtected),
            PlcBlockProtectionMethods.RemoveKnowHowProtection => ("knowHowProtected", false, current?.State.KnowHowProtected),
            PlcBlockProtectionMethods.DefineWriteProtection => ("writeProtectionDefined", true, current?.State.WriteProtectionDefined),
            PlcBlockProtectionMethods.ChangeWriteProtectionPassword => ("writeProtectionPassword", true, current?.State.WriteProtectionDefined),
            PlcBlockProtectionMethods.SetWriteProtection => ("writeProtectionEnabled", true, current?.State.WriteProtectionEnabled),
            _ => ("writeProtectionEnabled", false, current?.State.WriteProtectionEnabled),
        };
        var changes = new List<PlcFieldChange>
        {
            item.Operation == PlcBlockProtectionMethods.ChangeWriteProtectionPassword
                ? new PlcFieldChange(field, before is null ? null : before.Value ? "defined" : "not defined", "changed")
                : new PlcFieldChange(field, before is null ? null : before.Value ? "true" : "false", requested ? "true" : "false"),
            new PlcFieldChange("passwordVariable", null, item.PasswordVariable),
        };
        if (item.Operation == PlcBlockProtectionMethods.ChangeWriteProtectionPassword)
            changes.Add(new PlcFieldChange("newPasswordVariable", null, item.NewPasswordVariable));
        return (new PlcWriteEffect(item.Operation, target, changes, null, null, null, null), guards, null);
    }

    private static string Describe(PlcOperationRequest item, string blockPath) => item.Operation switch
    {
        PlcBlockProtectionMethods.SetKnowHowProtection => $"set_block_know_how_protection protects '{blockPath}' with the know-how password from its source (PlcBlockProtectionProvider.Protect); keep the source available.",
        PlcBlockProtectionMethods.RemoveKnowHowProtection => $"remove_block_know_how_protection removes the know-how protection of '{blockPath}' (PlcBlockProtectionProvider.Unprotect).",
        PlcBlockProtectionMethods.DefineWriteProtection => $"define_block_write_protection defines the write-protection password of '{blockPath}' (PlcBlockWriteProtectionProvider.Define); keep the source available.",
        PlcBlockProtectionMethods.SetWriteProtection => $"set_block_write_protection enables the write protection of '{blockPath}' (PlcBlockWriteProtectionProvider.Protect).",
        PlcBlockProtectionMethods.RemoveWriteProtection => $"remove_block_write_protection disables the write protection of '{blockPath}' (PlcBlockWriteProtectionProvider.Unprotect).",
        _ => $"change_block_write_protection_password replaces the write-protection password of '{blockPath}' (PlcBlockWriteProtectionProvider.Change); keep the new source available.",
    };

    /// <exception cref="JsonException">The payload does not decode as <see cref="PlcBlockProtectionInfo"/> or names another block.</exception>
    public static JsonElement DecodeRead(PlcOperationRequest operation, string payload)
        => CanonicalJson.NormalizeWorkerPayload<PlcBlockProtectionInfo>(payload, info =>
        {
            if (string.IsNullOrWhiteSpace(info.PlcName) || string.IsNullOrWhiteSpace(info.BlockPath) || string.IsNullOrWhiteSpace(info.BlockName)
                || info.State is null || info.Notes is null)
                throw new JsonException("The block protection result does not describe a block.");
            StandalonePayloadContract.RejectNullElements(info.Notes);
            CheckProvider(info.KnowHow, PlcBlockProtectionNames.KnowHowProvider);
            CheckProvider(info.WriteProtection, PlcBlockProtectionNames.WriteProvider);
            if (info.IsFailSafe != Contracts.Plc.Safety.PlcSafetyPolicy.IsFailSafeLanguage(info.ProgrammingLanguage))
                throw new JsonException("isFailSafe contradicts the programming language.");
            if (operation.BlockPath is { } path && !string.Equals(path.Split('/').Last(), info.BlockName, StringComparison.OrdinalIgnoreCase))
                throw new JsonException("The result names another block.");
        }).Element;

    private static void CheckProvider(PlcBlockProtectionProviderInfo? provider, string service)
    {
        if (provider is null || provider.Service != service || provider.Members is null || provider.Available && !provider.ServiceOffered
            || !provider.Available && (provider.Members.Count > 0 || provider.InvalidPasswordCharacters is not null))
            throw new JsonException("The provider description is inconsistent.");
        StandalonePayloadContract.RejectNullElements(provider.Members);
        if (provider.InvalidPasswordCharacters is { } characters)
        {
            StandalonePayloadContract.RejectNullElements(characters);
            if (characters.Count > PlcBlockProtectionNames.MaxInvalidCharacters || characters.Any(c => c.Length != 1)) throw new JsonException();
        }
    }

    /// <exception cref="JsonException">The payload does not decode as <see cref="PlcBlockProtectionWriteResultInfo"/> or contradicts the request.</exception>
    public static JsonElement DecodeWrite(PlcOperationRequest item, string payload)
        => CanonicalJson.NormalizeWorkerPayload<PlcBlockProtectionWriteResultInfo>(payload, result =>
        {
            var service = PlcBlockProtectionMethods.IsKnowHow(item.Operation) ? PlcBlockProtectionNames.KnowHowProvider : PlcBlockProtectionNames.WriteProvider;
            if (result.Operation != item.Operation || result.Service != service || string.IsNullOrWhiteSpace(result.PlcName) || string.IsNullOrWhiteSpace(result.BlockPath)
                || string.IsNullOrWhiteSpace(result.BlockName) || string.IsNullOrWhiteSpace(result.Member) || result.Before is null || result.After is null
                || result.ReadBack is not (PlcBlockProtectionNames.ReadBackMatched or PlcBlockProtectionNames.ReadBackUnreadable)
                || result.PasswordSources is null || result.Messages is null)
                throw new JsonException("The block protection result does not describe the request.");
            StandalonePayloadContract.RejectNullElements(result.PasswordSources);
            StandalonePayloadContract.RejectNullElements(result.Messages);
            var requested = new HashSet<string?>(new[] { item.PasswordVariable, item.NewPasswordVariable }, StringComparer.Ordinal);
            if (result.PasswordSources.Count == 0 || result.PasswordSources.Any(use => !requested.Contains(use.Source) || !Contracts.Shared.PasswordSources.IsSource(use.Source)))
                throw new JsonException("The result names a password source the request did not give.");
            var observed = Observed(result);
            if (result.ReadBack == PlcBlockProtectionNames.ReadBackMatched ? observed != result.Expected : observed is not null)
                throw new JsonException("The read-back contradicts the reported state.");
            if (!string.Equals(item.BlockPath?.Split('/').Last(), result.BlockName, StringComparison.OrdinalIgnoreCase))
                throw new JsonException("The result names another block.");
        }).Element;

    private static bool? Observed(PlcBlockProtectionWriteResultInfo result) => result.ReadBackProperty switch
    {
        PlcBlockProtectionNames.IsKnowHowProtected => result.After.KnowHowProtected,
        PlcBlockProtectionNames.IsDefined => result.After.WriteProtectionDefined,
        PlcBlockProtectionNames.IsProtected => result.After.WriteProtectionEnabled,
        _ => throw new JsonException("Unknown read-back property."),
    };

    public static PlcBlockProtectionWriteResultInfo? Result(PlcOperationRequest item, IReadOnlyDictionary<string, JsonElement> results)
        => PlcBlockProtectionMethods.IsWrite(item.Operation) && results.TryGetValue(item.OperationId, out var raw)
            ? CanonicalJson.Deserialize<PlcBlockProtectionWriteResultInfo>(raw.GetRawText())
            : null;

    /// <summary>A completed item whose worker read-back matched; an unreadable read-back stays trusted (nothing could be read).</summary>
    public static bool HasReadBack(PlcOperationRequest item, IReadOnlyDictionary<string, JsonElement> results)
        => Result(item, results) is { ReadBack: PlcBlockProtectionNames.ReadBackMatched };

    public static WriteOperationVerification Verify(PlcOperationRequest item, IReadOnlyDictionary<string, JsonElement> results)
    {
        var result = Result(item, results)!;
        var expected = $"{result.ReadBackProperty}={(result.Expected ? "true" : "false")}";
        var observed = Observed(result);
        var success = observed == result.Expected;
        return new(item.OperationId, success, Check, expected, observed is { } value ? $"{result.ReadBackProperty}={(value ? "true" : "false")}" : "(unreadable)",
            $"Operation '{item.OperationId}': the worker read {result.ReadBackProperty} of '{result.BlockPath}' back right after {result.Member}"
            + (item.Operation == PlcBlockProtectionMethods.ChangeWriteProtectionPassword ? "; the new password itself cannot be read back." : "."));
    }
}
