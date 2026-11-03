using System.Security.Cryptography;
using System.Text.Json;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;

namespace TiaMcpServer.PlcWebApi;

/// <summary>
/// plc_online_write on the guarded single-call pipeline, like <c>plc_download</c>: every actual
/// read-write call is confirmed once by the user (<see cref="ConfirmsEveryCall"/>), full runs under
/// policy, and a dry run stops at <c>preview</c>. Planning is offline (configuration, input, local
/// files), so a dry run never contacts a PLC. Items run in order and the first failure stops the
/// call; nothing is rolled back. There is no verification read.
/// </summary>
public sealed class PlcWebApiWriteDomain
    : IWriteDomain<PlcOnlineWriteRequest, PlcWebApiWriteEffect, object, PlcOnlineWriteResponse>
{
    public const string Tool = "plc_online_write";

    /// <summary>Largest backup file restore_backup accepts.</summary>
    public const long MaxRestoreBytes = 1L << 30;

    /// <summary>Longest tag value (JSON text) the confirmation prompt shows in full; longer ones show length and SHA-256.</summary>
    public const int MaxPromptValueChars = 512;

    private const string InspectBeforeRetry =
        "An online change may already have reached the controller. Inspect it with plc_online_read before retrying; nothing was rolled back or replayed.";

    private readonly PlcWebApiService _service;
    private readonly CancellationToken _cancellationToken;
    private readonly Dictionary<string, PlcWebApiWriteEffect> _effects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonElement> _failureEvidence = new(StringComparer.Ordinal);

    public PlcWebApiWriteDomain(PlcWebApiService service, CancellationToken cancellationToken = default)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _cancellationToken = cancellationToken;
    }

    public string ToolName => Tool;

    public string ContractVersion => PlcWebApiContractVersion.Current;

    public bool ConfirmsEveryCall => true;

    /// <summary>
    /// One line per item: the effect first (its caller-supplied values quoted and escaped), then the
    /// target, and the caller's operationId last, so no caller text can come before or hide an effect.
    /// </summary>
    public string DescribeForConfirmation(IReadOnlyList<PlcOnlineWriteRequest> items, IReadOnlyList<ItemPlan<PlcWebApiWriteEffect>> plans)
        => $"Confirm plc_online_write: {plans.Count} online change(s) on real controller(s), in this order; the first failure stops the call and nothing is rolled back.\n"
           + string.Join("\n", items.Zip(plans, (item, plan) => (item, plan.Effect!)).Select((entry, index) =>
               $"{index + 1}. {entry.Item2.Description} on target {ConfirmationText.Quote(entry.Item2.Target)} at {ConfirmationText.Quote(entry.Item2.Address)} "
               + $"({entry.Item2.ApiMethod}); operationId {ConfirmationText.Quote(entry.item.OperationId)}"));

    public WriteValidation Validate(IReadOnlyList<PlcOnlineWriteRequest> items, McpAccessMode accessMode)
        => WriteValidation.Compose(
            PlcWebApiCatalog.ValidateWrite(items),
            () => CanonicalJson.Serialize(items).Length * 2,
            () => PlcWebApiCatalog.ValidateAccessMode(items, accessMode),
            ToolName);

    public Task<WritePlan<PlcWebApiWriteEffect>> PlanAsync(IReadOnlyList<PlcOnlineWriteRequest> items)
    {
        var plans = new List<ItemPlan<PlcWebApiWriteEffect>>(items.Count);
        foreach (var item in items)
        {
            var target = _service.Configuration.Find(item.Target);
            if (target is null)
                return Task.FromResult(WritePlan<PlcWebApiWriteEffect>.Fail(WorkerFailureCategories.TargetNotFound,
                    $"{item.OperationId}: {PlcWebApiWarnings.NotConfigured(_service.Configuration, item.Target)}"));
            var (effect, error) = Effect(item, target);
            if (error is not null) return Task.FromResult(WritePlan<PlcWebApiWriteEffect>.Fail(error.Category, $"{item.OperationId}: {error.Message}"));
            _effects[item.OperationId] = effect!;
            plans.Add(ItemPlan<PlcWebApiWriteEffect>.Resolved(effect!, new[]
            {
                new CheckedPrecondition("target_configured", target.Name,
                    $"{target.Address}, certificate validation {target.CertificateValidationName}, {Authentication(target)}", Satisfied: true),
            }));
        }

        return Task.FromResult(WritePlan<PlcWebApiWriteEffect>.Ok(plans));
    }

    public IReadOnlyList<FiredGuard> EvaluateGuards(IReadOnlyList<PlcOnlineWriteRequest> items, IReadOnlyList<ItemPlan<PlcWebApiWriteEffect>> plans)
    {
        var effects = plans.Select(plan => plan.Effect!).ToArray();
        var guards = new List<FiredGuard>
        {
            new(PlcWebApiGuardDefinitions.OnlineChangesRealController, null,
                $"This call changes {effects.Length} item(s) online on the real controller(s) "
                + string.Join(", ", effects.Select(TargetText).Distinct(StringComparer.Ordinal))
                + ". The changes act on the running machine immediately; nothing is rolled back."),
        };
        for (var index = 0; index < items.Count; index++)
        {
            var effect = effects[index];
            if (effect.Operation == "change_operating_mode" && effect.OperatingMode == "stop")
                guards.Add(new(PlcWebApiGuardDefinitions.OperatingModeStop, items[index].OperationId,
                    $"STOP is requested for {TargetText(effect)}: the user program stops executing and the outputs take their configured STOP behaviour."));
            if (effect.Operation == "restore_backup")
                guards.Add(new(PlcWebApiGuardDefinitions.RestoreBackupReplacesProgram, items[index].OperationId,
                    $"A backup restore stops and restarts {TargetText(effect)}, and its program, hardware configuration and data are replaced by the content of "
                    + $"{ConfirmationText.Quote(effect.File!.Path)} ({effect.File.Bytes} bytes, sha256 {effect.File.Sha256})."));
        }

        return guards;
    }

    public Task<ItemReplan<PlcWebApiWriteEffect>> ReplanAsync(PlcOnlineWriteRequest item)
        => throw new InvalidOperationException("plc_online_write plans declare no dependencies; ReplanAsync must never be called.");

    public async Task<WorkerCallResult> MutateAsync(PlcOnlineWriteRequest item)
    {
        var effect = _effects[item.OperationId];
        var target = _service.Configuration.Find(item.Target)!;
        var ct = _cancellationToken;
        try
        {
            switch (item.Operation)
            {
                case "change_operating_mode":
                    return Accepted(await _service.CallAsync(target, (c, t) => c.RequestChangeOperatingModeAsync(effect.OperatingMode!, t), ct).ConfigureAwait(false),
                        effect, new PlcWebApiOperatingModeChange(effect.OperatingMode!, true,
                            "The PLC accepted the request; plc_online_read read_operating_mode shows when the CPU has reached the mode."));
                case "write_tags":
                    return await WriteTagsAsync(item, target, effect, ct).ConfigureAwait(false);
                case "acknowledge_alarm":
                    return Accepted(await _service.CallAsync(target, (c, t) => c.AcknowledgeAlarmAsync(effect.AlarmId!, t), ct).ConfigureAwait(false),
                        effect, new PlcWebApiAcknowledgement(effect.AlarmId!, true));
                case "flash_leds":
                    return Accepted(await _service.CallAsync(target, (c, t) => c.FlashLedsAsync((uint)effect.Hwid!.Value, t), ct).ConfigureAwait(false),
                        effect, new PlcWebApiFlashLeds(effect.Hwid!.Value, true));
                case "set_system_time":
                    var timestamp = DateTimeOffset.Parse(effect.Timestamp!, System.Globalization.CultureInfo.InvariantCulture).UtcDateTime;
                    return Accepted(await _service.CallAsync(target, (c, t) => c.SetSystemTimeAsync(timestamp, t), ct).ConfigureAwait(false),
                        effect, new PlcWebApiSystemTimeChange(effect.Timestamp!, true));
                case "create_backup":
                    return await CreateBackupAsync(target, effect, ct).ConfigureAwait(false);
                case "restore_backup":
                    return await RestoreBackupAsync(target, effect, ct).ConfigureAwait(false);
                default:
                    throw new InvalidOperationException($"plc_online_write operation '{item.Operation}' passed validation without an executor.");
            }
        }
        catch (PlcWebApiException exception)
        {
            if (exception.Error is { } error && !_failureEvidence.ContainsKey(item.OperationId))
                _failureEvidence[item.OperationId] = CanonicalJson.ToElement(new PlcWebApiFailureResult(error));
            return WorkerCallResult.Fail(exception.Category, exception.Message);
        }
    }

    public StructuredOperationItem Project(PlcOnlineWriteRequest item, WorkerCallResult result)
    {
        var warnings = PlcWebApiWarnings.ForTarget(_service.Configuration.Find(item.Target)).Concat(result.Warnings).ToArray();
        if (!result.Success)
            return new StructuredOperationItem(item.OperationId, item.Operation, OperationBatchStatus.Failed,
                _failureEvidence.TryGetValue(item.OperationId, out var evidence) ? evidence : null,
                new StructuredOperationFailure(result.FailureCategory ?? WorkerFailureCategories.WebApiRejected, result.Error ?? "The online change failed."),
                null, null, warnings);
        using var document = JsonDocument.Parse(result.Payload);
        return new StructuredOperationItem(item.OperationId, item.Operation, OperationBatchStatus.Succeeded, document.RootElement.Clone(),
            null, null, null, warnings);
    }

    public Task<object?> VerifyAsync(StructuredOperationBatch batch) => Task.FromResult<object?>(null);

    public PlcOnlineWriteResponse Compose(WriteReport<PlcWebApiWriteEffect, object> report)
    {
        var warnings = report.Warnings.ToList();
        warnings.AddRange(PlcWebApiWarnings.ForTargets(_service.Configuration, report.Effects.Select(effect => effect.Effect.Target)));
        if (report.Phase == WritePhases.Applied && report.Batch?.IsFullySuccessful != true) warnings.Add(InspectBeforeRetry);
        warnings = warnings.Distinct(StringComparer.Ordinal).ToList();
        var batch = report.Batch is null ? null : StructuredOperationBatchPayloadBudget.Apply(report.Batch,
            value => Response(report, warnings, value), ToolName,
            _ => "Inspect the controller with plc_online_read before retrying; this write may have been applied.");
        return Response(report, warnings, batch);
    }

    private PlcOnlineWriteResponse Response(WriteReport<PlcWebApiWriteEffect, object> report, IReadOnlyList<string> warnings, StructuredOperationBatch? batch)
        => new(ToolName, ContractVersion, report.Phase, report.Success && (batch is null || batch.IsFullySuccessful),
            report.Error, warnings, report.Guards, report.Effects, batch);

    private (PlcWebApiWriteEffect? Effect, WriteToolError? Error) Effect(PlcOnlineWriteRequest item, PlcWebApiTarget target)
    {
        PlcWebApiWriteEffect Make(string method, string description, string? mode = null, IReadOnlyList<PlcWebApiPlannedTag>? tags = null,
            string? representation = null, string? alarmId = null, long? hwid = null, string? timestamp = null, PlcWebApiPlannedFile? file = null)
            => new(item.Operation, target.Name, target.Address, target.CertificateValidationName, Authentication(target), method, description,
                mode, tags, representation, alarmId, hwid, timestamp, file);

        switch (item.Operation)
        {
            case "change_operating_mode":
                return (Make("Plc.RequestChangeOperatingMode", $"request operating mode {item.OperatingMode!.ToUpperInvariant()}", mode: item.OperatingMode), null);
            case "write_tags":
                var tags = item.Tags!.Select(tag => new PlcWebApiPlannedTag(tag.Name!, tag.Value!.Value.Clone())).ToArray();
                return (Make("PlcProgram.Write", $"write {tags.Length} tag(s) ({item.Representation ?? "simple"}): "
                        + string.Join(", ", tags.Select(tag => $"{ConfirmationText.Quote(tag.Name)} = {PromptValue(tag.Value)}")),
                    tags: tags, representation: item.Representation ?? "simple"), null);
            case "acknowledge_alarm":
                return (Make("Alarms.Acknowledge", $"acknowledge alarm {ConfirmationText.Quote(item.AlarmId)}", alarmId: item.AlarmId), null);
            case "flash_leds":
                return (Make("Modules.FlashLeds", $"flash the LEDs of module hwid {item.Hwid}", hwid: item.Hwid), null);
            case "set_system_time":
                PlcWebApiCatalog.TryParseTimestamp(item.Timestamp, out var utc);
                var formatted = PlcWebApiCatalog.FormatTimestamp(utc);
                return (Make("Plc.SetSystemTime", $"set the PLC system time to {formatted}", timestamp: formatted), null);
            case "create_backup":
                return BackupTarget(item, out var created) is { } createError
                    ? (null, createError)
                    : (Make("Plc.CreateBackup", $"create a backup{(created!.Exists ? " and replace the existing file" : " and write it to the new file")} {ConfirmationText.Quote(created.Path)}", file: created), null);
            case "restore_backup":
                if (target.IsAnonymous)
                    return (null, new WriteToolError(WorkerFailureCategories.ValidationError,
                        $"restore_backup needs a target with userEnv/passwordEnv: the client's restore helper logs in again with that user after each PLC restart, and target '{target.Name}' is anonymous."));
                return RestoreSource(item.Path!, out var source) is { } restoreError
                    ? (null, restoreError)
                    : (Make("Plc.RestoreBackup", $"restore the PLC (stop, replace program, hardware configuration and data, restart) from the backup {ConfirmationText.Quote(source!.Path)} ({source.Bytes} bytes, sha256 {source.Sha256})", file: source), null);
            default:
                throw new InvalidOperationException($"plc_online_write operation '{item.Operation}' passed validation without a planner.");
        }
    }

    private static WriteToolError? BackupTarget(PlcOnlineWriteRequest item, out PlcWebApiPlannedFile? file)
    {
        file = null;
        var path = Path.GetFullPath(item.Path!);
        var overwrite = item.Overwrite == true;
        var directory = Path.GetDirectoryName(path);
        if (directory is null || !Directory.Exists(directory))
            return new WriteToolError(WorkerFailureCategories.ValidationError, $"create_backup: the folder of '{path}' does not exist.");
        if (Directory.Exists(path))
            return new WriteToolError(WorkerFailureCategories.ValidationError, $"create_backup: '{path}' is a folder; give a file path.");
        var exists = File.Exists(path);
        if (exists && !overwrite)
            return new WriteToolError(WorkerFailureCategories.ValidationError, $"create_backup: '{path}' already exists; set overwrite:true to replace it.");
        file = new PlcWebApiPlannedFile(path, exists, overwrite, null, null);
        return null;
    }

    private static WriteToolError? RestoreSource(string requestedPath, out PlcWebApiPlannedFile? file)
    {
        file = null;
        var path = Path.GetFullPath(requestedPath);
        try
        {
            // The size is checked before the file is read: an oversized file is refused without hashing it.
            var info = new FileInfo(path);
            if (!info.Exists)
                return new WriteToolError(WorkerFailureCategories.ValidationError, $"restore_backup: the backup file '{path}' does not exist.");
            if (info.Length == 0) return new WriteToolError(WorkerFailureCategories.ValidationError, $"restore_backup: '{path}' is empty.");
            if (info.Length > MaxRestoreBytes)
                return new WriteToolError(WorkerFailureCategories.ValidationError, $"restore_backup: '{path}' exceeds {MaxRestoreBytes} bytes.");
            var (bytes, hash) = Fingerprint(path, MaxRestoreBytes);
            if (bytes is null)
                return new WriteToolError(WorkerFailureCategories.ValidationError, $"restore_backup: '{path}' grew beyond {MaxRestoreBytes} bytes while it was read.");
            file = new PlcWebApiPlannedFile(path, true, false, bytes, hash);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new WriteToolError(WorkerFailureCategories.ValidationError, $"restore_backup: '{path}' could not be read: {exception.Message}");
        }
    }

    private async Task<WorkerCallResult> WriteTagsAsync(PlcOnlineWriteRequest item, PlcWebApiTarget target, PlcWebApiWriteEffect effect, CancellationToken ct)
    {
        var results = new List<PlcWebApiTagWriteResult>(effect.Tags!.Count);
        foreach (var tag in effect.Tags)
        {
            try
            {
                var accepted = await _service.CallAsync(target, (c, t) => c.WriteVariableAsync(tag.Name, tag.Value, effect.Representation, t), ct).ConfigureAwait(false);
                if (!accepted)
                    throw new PlcWebApiException(WorkerFailureCategories.WebApiRejected, $"The PLC answered PlcProgram.Write for {tag.Name} with false.");
                results.Add(new PlcWebApiTagWriteResult(tag.Name, true, null, null, null));
            }
            catch (PlcWebApiException exception)
            {
                results.Add(new PlcWebApiTagWriteResult(tag.Name, false, exception.Category, exception.Message, exception.Error));
                _failureEvidence[item.OperationId] = CanonicalJson.ToElement(new PlcWebApiWriteTagsResult(effect.Tags.Count, results.Count - 1, results));
                return WorkerCallResult.Fail(exception.Category,
                    $"Writing stopped at tag {results.Count} of {effect.Tags.Count} ({tag.Name}); {results.Count - 1} earlier tag(s) stay written. {exception.Message}");
            }
        }

        return WorkerCallResult.Ok(CanonicalJson.Serialize(new PlcWebApiWriteTagsResult(effect.Tags.Count, results.Count, results)));
    }

    private async Task<WorkerCallResult> CreateBackupAsync(PlcWebApiTarget target, PlcWebApiWriteEffect effect, CancellationToken ct)
    {
        var file = effect.File!;
        if (!file.Overwrite && File.Exists(file.Path))
            return WorkerCallResult.Fail(WorkerFailureCategories.StateChanged,
                $"'{file.Path}' appeared after the call was planned and overwrite is false; nothing was requested from the PLC.");
        // Plc.CreateBackup is one request, so a refusal for missing permission (an expired session) was not
        // executed and may be repeated after a new login. The ticket download is never repeated.
        var ticket = await _service.CallAsync(target, (c, t) => c.RequestBackupAsync(t), ct).ConfigureAwait(false);
        var bytes = await _service.CallAsync(target, (c, t) => c.DownloadBackupAsync(ticket, t), ct, retryAfterRelogin: false).ConfigureAwait(false);
        var temporary = file.Path + ".tia-mcp-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, CancellationToken.None).ConfigureAwait(false);
            File.Move(temporary, file.Path, overwrite: file.Overwrite);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDelete(temporary);
            return WorkerCallResult.Fail(WorkerFailureCategories.StateChanged,
                $"The PLC produced a backup of {bytes.Length} bytes, but '{file.Path}' could not be written: {exception.Message}");
        }

        return WorkerCallResult.Ok(CanonicalJson.Serialize(new PlcWebApiBackupFile(file.Path, bytes.Length, Sha256(bytes), file.Exists)));
    }

    private async Task<WorkerCallResult> RestoreBackupAsync(PlcWebApiTarget target, PlcWebApiWriteEffect effect, CancellationToken ct)
    {
        var file = effect.File!;
        (long? Bytes, string? Hash) current;
        try
        {
            // A changed size is a changed file: no need to read it again.
            var length = new FileInfo(file.Path).Length;
            current = length == file.Bytes ? Fingerprint(file.Path, length) : (length, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return WorkerCallResult.Fail(WorkerFailureCategories.StateChanged, $"'{file.Path}' could not be read again before the restore: {exception.Message}");
        }

        if (current.Bytes != file.Bytes || current.Hash != file.Sha256)
            return WorkerCallResult.Fail(WorkerFailureCategories.StateChanged,
                $"'{file.Path}' changed after the call was planned ("
                + (current.Bytes is not { } now ? "it grew while it was read again"
                    : current.Hash is null ? $"now {now} bytes" : $"now sha256 {current.Hash}")
                + "); nothing was sent to the PLC.");
        var credentials = _service.ReadCredentials(target)!.Value;
        try
        {
            await _service.CallAsync(target, async (c, t) =>
            {
                await c.RestoreBackupAsync(file.Path, credentials.User, credentials.Password, PlcWebApiService.RestoreTimeout, t).ConfigureAwait(false);
                return true;
            }, ct, retryAfterRelogin: false).ConfigureAwait(false);
        }
        catch (PlcWebApiException exception)
        {
            // The helper uploads, waits for restarts and logs in again; a failure at any of those steps
            // does not tell how far the PLC got.
            throw new PlcWebApiException(exception.Category,
                $"restore_backup did not complete cleanly: {exception.Message} The PLC may already have been put into restore mode, "
                + "restored or restarted; inspect it with plc_online_read (ping, read_operating_mode, read_diagnostic_buffer) before anything else.",
                exception.Error);
        }

        return WorkerCallResult.Ok(CanonicalJson.Serialize(new PlcWebApiRestoreResult(file.Path, file.Bytes!.Value, file.Sha256!, true,
            "The client's restore helper uploaded the backup, waited for the PLC to restart and logged in again. Check read_operating_mode and the diagnostic buffer.")));
    }

    private static WorkerCallResult Accepted<TResult>(bool accepted, PlcWebApiWriteEffect effect, TResult result)
        => accepted
            ? WorkerCallResult.Ok(CanonicalJson.Serialize(result))
            : WorkerCallResult.Fail(WorkerFailureCategories.WebApiRejected, $"The PLC answered {effect.ApiMethod} with false (not accepted).");

    private static string Authentication(PlcWebApiTarget target)
        => target.IsAnonymous ? "anonymous user" : $"user from {target.UserEnv}";

    /// <summary>Size and SHA-256 of the file, reading at most <paramref name="maxBytes"/>; (null, null) when it is longer.</summary>
    private static (long? Bytes, string? Hash) Fingerprint(string path, long maxBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > maxBytes) return (null, null);
            hash.AppendData(buffer, 0, read);
        }

        return (total, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>
    /// A tag value for the prompt: its JSON text in full (unsafe characters escaped) up to
    /// <see cref="MaxPromptValueChars"/> characters, otherwise its length and the SHA-256 of that JSON text
    /// (UTF-8), which the human can check against the value in a dryRun preview's effects.
    /// </summary>
    internal static string PromptValue(JsonElement value)
    {
        var text = value.GetRawText();
        return text.Length <= MaxPromptValueChars
            ? ConfirmationText.Line(text)
            : $"<JSON value of {text.Length} characters, sha256 {Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant()}>";
    }

    private static string TargetText(PlcWebApiWriteEffect effect)
        => $"{ConfirmationText.Quote(effect.Target)} ({ConfirmationText.Quote(effect.Address)})";

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}

public sealed record PlcWebApiOperatingModeChange(string RequestedMode, bool Accepted, string Note);

public sealed record PlcWebApiAcknowledgement(string AlarmId, bool Accepted);

public sealed record PlcWebApiFlashLeds(long Hwid, bool Accepted);

public sealed record PlcWebApiSystemTimeChange(string Timestamp, bool Accepted);

public sealed record PlcWebApiTagWriteResult(string Name, bool Written, string? FailureCategory, string? FailureMessage, PlcWebApiErrorInfo? WebApiError);

public sealed record PlcWebApiWriteTagsResult(int Requested, int Written, IReadOnlyList<PlcWebApiTagWriteResult> Tags);

public sealed record PlcWebApiBackupFile(string Path, long Bytes, string Sha256, bool Replaced);

public sealed record PlcWebApiRestoreResult(string Path, long Bytes, string Sha256, bool Restored, string Note);
