using System.Text.Json;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;

namespace TiaMcpServer.OpennessWorker.Openness.Block;

internal static class BlockImportOutcomeProjection
{
    private const int MaxPlcs = BlockImportOutcomeLimits.MaxPlcs;
    private const int MaxMessages = BlockImportOutcomeLimits.MaxMessages;
    private const int MaxNotes = BlockImportOutcomeLimits.MaxNotes;
    private const int MaxFieldChars = BlockImportOutcomeLimits.MaxFieldChars;
    private const int MaxDiagnosticChars = BlockImportOutcomeLimits.MaxDiagnosticChars;
    private const int MaxSerializedChars = BlockImportOutcomeLimits.MaxSerializedChars;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        // Match the host validator's TiaJson.Presentation representation: camelCase with nulls.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static BlockImportOutcomeInfo Project(BlockImportOutcomeInfo source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));

        var omitted = source.CompileDetailsOmitted;
        var report = source.CompileReport is null ? null : CopyReport(source.CompileReport, ref omitted);
        var result = CopyOutcome(source, report, omitted);

        while (JsonSerializer.Serialize(result, JsonOptions).Length > MaxSerializedChars)
        {
            if (report is null)
                throw new InvalidOperationException("Block import outcome metadata exceeds its response limit.");

            if (RemoveLastMessage(report, priority: 2) || RemoveLastNote(report)
                || RemoveLastMessage(report, priority: 1) || RemoveLastMessage(report, priority: 0)
                || ShortenMetadata(report))
            {
                omitted = true;
                result = CopyOutcome(source, report, omitted);
                continue;
            }

            if (report.Plcs.Count > 0)
            {
                report.Plcs.RemoveAt(report.Plcs.Count - 1);
                omitted = true;
                result = CopyOutcome(source, report, omitted);
                continue;
            }

            throw new InvalidOperationException("Block import outcome metadata exceeds its response limit.");
        }

        return result;
    }

    private static BlockImportOutcomeInfo CopyOutcome(
        BlockImportOutcomeInfo source,
        CompileCheckReport? report,
        bool omitted) => new BlockImportOutcomeInfo
    {
        ImportStage = source.ImportStage,
        ImportResultState = source.ImportResultState,
        TargetMutationCommitted = source.TargetMutationCommitted,
        CompileStage = source.CompileStage,
        CompileReport = report,
        CompileDetailsOmitted = omitted,
        FinalReadStage = source.FinalReadStage,
        TargetPresent = source.TargetPresent,
        ContentRelation = "unknown",
        TemporarySourceState = source.TemporarySourceState
    };

    /// <summary>
    /// A bounded copy of the compile report with TIA Portal's own texts (control characters replaced, trimmed, at most
    /// <see cref="MaxFieldChars"/> each). Notes (the worker's own texts) are kept first; then messages are chosen by
    /// severity (errors, then warnings, then information, each in report order) while the message count and the
    /// diagnostic character budget allow, and are emitted in report order. Everything else is omitted and flagged.
    /// </summary>
    private static CompileCheckReport CopyReport(CompileCheckReport source, ref bool omitted)
    {
        var report = new CompileCheckReport
        {
            Scope = SafeMetadata(source.Scope, required: true, ref omitted) ?? string.Empty,
            BlockPath = SafeMetadata(source.BlockPath, required: false, ref omitted),
            TotalErrorCount = source.TotalErrorCount,
            TotalWarningCount = source.TotalWarningCount,
            OverallState = SafeMetadata(source.OverallState, required: true, ref omitted) ?? string.Empty
        };

        var noteCount = 0;
        var diagnosticChars = 0;
        var candidates = new List<(PlcCompileInfo Plc, int Order, int Priority, CompileMessageInfo Message)>();
        var sourcePlcs = source.Plcs ?? new List<PlcCompileInfo>();
        for (var plcIndex = 0; plcIndex < sourcePlcs.Count; plcIndex++)
        {
            if (report.Plcs.Count >= MaxPlcs)
            {
                omitted = true;
                break;
            }

            var sourcePlc = sourcePlcs[plcIndex];
            if (sourcePlc is null)
            {
                omitted = true;
                continue;
            }

            var plc = new PlcCompileInfo
            {
                PlcName = SafeMetadata(sourcePlc.PlcName, required: true, ref omitted) ?? string.Empty,
                DeviceName = SafeMetadata(sourcePlc.DeviceName, required: false, ref omitted),
                State = SafeMetadata(sourcePlc.State, required: true, ref omitted) ?? string.Empty,
                ErrorCount = sourcePlc.ErrorCount,
                WarningCount = sourcePlc.WarningCount
            };

            foreach (var sourceNote in sourcePlc.DiagnosticNotes ?? new List<string>())
            {
                var note = sourceNote is null ? null : SafeMetadata(sourceNote, required: true, ref omitted);
                if (note is null || noteCount >= MaxNotes || diagnosticChars + note.Length > MaxDiagnosticChars)
                {
                    omitted = true;
                    continue;
                }

                plc.DiagnosticNotes.Add(note);
                diagnosticChars += note.Length;
                noteCount++;
            }

            foreach (var sourceMessage in sourcePlc.Messages ?? new List<CompileMessageInfo>())
            {
                if (sourceMessage is null)
                {
                    omitted = true;
                    continue;
                }

                var message = new CompileMessageInfo
                {
                    Description = SafeMetadata(sourceMessage.Description, required: true, ref omitted) ?? string.Empty,
                    Path = SafeMetadata(sourceMessage.Path, required: true, ref omitted) ?? string.Empty,
                    Severity = SafeMetadata(sourceMessage.Severity, required: true, ref omitted) ?? string.Empty
                };
                candidates.Add((plc, candidates.Count, Priority(message), message));
            }

            report.Plcs.Add(plc);
        }

        var selected = new HashSet<int>();
        foreach (var candidate in candidates.OrderBy(c => c.Priority).ThenBy(c => c.Order))
        {
            var chars = candidate.Message.Description.Length + candidate.Message.Path.Length;
            if (selected.Count >= MaxMessages || diagnosticChars + chars > MaxDiagnosticChars)
            {
                omitted = true;
                continue;
            }

            selected.Add(candidate.Order);
            diagnosticChars += chars;
        }

        foreach (var candidate in candidates.Where(c => selected.Contains(c.Order)))
            candidate.Plc.Messages.Add(candidate.Message);

        if (sourcePlcs.Count > report.Plcs.Count)
            omitted = true;
        return report;
    }

    private static int Priority(CompileMessageInfo message)
        => message.Severity == "Error" ? 0 : message.Severity == "Warning" ? 1 : 2;

    private static string? SafeMetadata(string? value, bool required, ref bool omitted)
    {
        if (value is null)
            return required ? string.Empty : null;

        var sanitized = ReplaceControls(value).Trim();
        if (sanitized.Length > MaxFieldChars)
        {
            omitted = true;
            sanitized = TakeWithoutSplittingSurrogate(sanitized, MaxFieldChars);
        }
        return sanitized;
    }

    private static string ReplaceControls(string value)
    {
        var chars = value.ToCharArray();
        for (var index = 0; index < chars.Length; index++)
        {
            if (char.IsControl(chars[index]))
                chars[index] = ' ';
        }
        return new string(chars);
    }

    private static string TakeWithoutSplittingSurrogate(string value, int length)
    {
        length = Math.Min(length, value.Length);
        if (length > 0 && length < value.Length
            && char.IsHighSurrogate(value[length - 1]) && char.IsLowSurrogate(value[length]))
            length--;
        return value.Substring(0, length);
    }

    private static bool RemoveLastNote(CompileCheckReport report)
    {
        for (var index = report.Plcs.Count - 1; index >= 0; index--)
        {
            var notes = report.Plcs[index].DiagnosticNotes;
            if (notes.Count == 0) continue;
            notes.RemoveAt(notes.Count - 1);
            return true;
        }
        return false;
    }

    /// <summary>Removes the last message of <paramref name="priority"/> (0 error, 1 warning, 2 information).</summary>
    private static bool RemoveLastMessage(CompileCheckReport report, int priority)
    {
        for (var index = report.Plcs.Count - 1; index >= 0; index--)
        {
            var messages = report.Plcs[index].Messages;
            var last = messages.FindLastIndex(message => Priority(message) == priority);
            if (last < 0) continue;
            messages.RemoveAt(last);
            return true;
        }
        return false;
    }

    private static bool ShortenMetadata(CompileCheckReport report)
    {
        if (CanShorten(report.Scope))
        {
            report.Scope = Shortened(report.Scope);
            return true;
        }
        if (CanShorten(report.BlockPath))
        {
            report.BlockPath = Shortened(report.BlockPath!);
            return true;
        }

        foreach (var plc in report.Plcs)
        {
            if (CanShorten(plc.PlcName))
            {
                plc.PlcName = Shortened(plc.PlcName);
                return true;
            }
            if (CanShorten(plc.DeviceName))
            {
                plc.DeviceName = Shortened(plc.DeviceName!);
                return true;
            }
        }
        return false;
    }

    private static bool CanShorten(string? value) => value is { Length: > 32 };

    private static string Shortened(string value) =>
        TakeWithoutSplittingSurrogate(value, Math.Max(32, value.Length / 2));
}
