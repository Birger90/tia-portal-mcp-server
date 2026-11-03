using System.Reflection;
using System.Text.Json;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;

namespace TiaMcpServer.OpennessWorker.Openness.Block;

internal static class CompileReportProjection
{
    private const int MaxDepth = 16;
    private const int MaxMessages = 200;
    private const int MaxFieldCharacters = 1024;
    private const int MaxTextCharacters = 32000;
    private const int MaxSerializedCharacters = 60000;
    private const string OmissionNote = "Some compiler diagnostics were omitted or shortened because a report limit was reached or a detail could not be read.";

    // Default escaping and retained nulls conservatively cover the worker's compact camel-case
    // payload (which omits nulls) and the host's presentation of this document.
    private static readonly JsonSerializerOptions ReportJson = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal sealed class Budget
    {
        internal int MessageCount;
        internal int TextCharacters;
        internal bool Exhausted => MessageCount >= MaxMessages || TextCharacters >= MaxTextCharacters;

        internal string TakeText(string text, Projection projection)
        {
            var length = Math.Min(text.Length, Math.Min(MaxFieldCharacters, MaxTextCharacters - TextCharacters));
            if (length < text.Length)
            {
                projection.WasTruncated = true;
                if (length > 0 && char.IsHighSurrogate(text[length - 1]) && char.IsLowSurrogate(text[length]))
                    length--;
            }

            TextCharacters += length;
            return length == text.Length ? text : text.Substring(0, length);
        }
    }

    internal sealed class Projection
    {
        public List<CompileMessageInfo> Messages { get; } = new List<CompileMessageInfo>();
        public bool WasTruncated { get; set; }
    }

    /// <summary>
    /// Flattens the compiler message tree parent-first. TIA Portal nests the real diagnostics under container
    /// nodes (the PLC, "Hardware configuration", a rack, a block folder) whose description is empty or blank and
    /// whose state repeats the worst state below them (live 2026-11-02); such a container is not a message and is
    /// not emitted. Its path is carried instead: every emitted message's path is the chain of the non-blank paths
    /// of its ancestors and its own (<see cref="CompileMessagePaths.Extend"/>), so a leaf with an empty path still
    /// says where it belongs. A message whose description could not be read is kept (with an empty description).
    /// Reported error and warning totals are TIA Portal's and never recomputed from the emitted rows.
    /// </summary>
    public static Projection Flatten<TMessage>(
        IEnumerable<TMessage> roots,
        Func<TMessage, string> readDescription,
        Func<TMessage, string> readPath,
        Func<TMessage, string> readSeverity,
        Func<TMessage, IEnumerable<TMessage>> readChildren,
        Budget budget)
    {
        var projection = new Projection();
        Visit(roots, 1, string.Empty);
        return projection;

        // Depth is one-based. On exhaustion, look at at most one additional element to
        // distinguish exact-boundary completion (including an empty next PLC) from omission.
        bool Visit(IEnumerable<TMessage> messages, int depth, string context)
        {
            try
            {
                foreach (var message in messages)
                {
                    if (depth > MaxDepth)
                    {
                        projection.WasTruncated = true;
                        return true;
                    }
                    if (budget.Exhausted)
                    {
                        projection.WasTruncated = true;
                        return false;
                    }

                    var descriptionRead = TryReadText(readDescription, message, out var description);
                    var path = CompileMessagePaths.Extend(context, ReadText(readPath, message));
                    if (!descriptionRead || !string.IsNullOrWhiteSpace(description))
                    {
                        budget.MessageCount++;
                        projection.Messages.Add(new CompileMessageInfo
                        {
                            Description = budget.TakeText(description, projection),
                            Path = budget.TakeText(path, projection),
                            Severity = ReadText(readSeverity, message, "Information")
                        });
                    }

                    IEnumerable<TMessage> children;
                    try
                    {
                        children = readChildren(message);
                    }
                    catch (Exception ex) when (IsExpectedDetailFailure(ex))
                    {
                        projection.WasTruncated = true;
                        continue;
                    }
                    if (!Visit(children, depth + 1, path))
                        return false;
                }
            }
            catch (Exception ex) when (IsExpectedDetailFailure(ex))
            {
                // Enumeration/disposal can also cross the Siemens remoting boundary.
                projection.WasTruncated = true;
            }

            return true;
        }

        string ReadText(Func<TMessage, string> read, TMessage message, string fallback = "")
        {
            TryReadText(read, message, out var text, fallback);
            return text;
        }

        bool TryReadText(Func<TMessage, string> read, TMessage message, out string text, string fallback = "")
        {
            try
            {
                text = read(message) ?? fallback;
                return true;
            }
            catch (Exception ex) when (IsExpectedDetailFailure(ex))
            {
                projection.WasTruncated = true;
                text = fallback;
                return false;
            }
        }
    }

    private static bool IsExpectedDetailFailure(Exception exception)
    {
        // Reflection is a transport wrapper, not evidence that a failure is recoverable.
        // Keep this projection seam Siemens-free and reject derived/infrastructure faults.
        while (exception is TargetInvocationException reflection && reflection.InnerException != null)
            exception = reflection.InnerException;
        return exception.GetType() == typeof(InvalidOperationException);
    }

    public static void NoteOmission(PlcCompileInfo plc)
    {
        if (!plc.DiagnosticNotes.Contains(OmissionNote))
            plc.DiagnosticNotes.Add(OmissionNote);
    }

    public static void BoundSerializedReport(CompileCheckReport report)
    {
        // Raw field limits alone do not bound JSON: one UTF-16 character can become six
        // serialized characters. Remove trailing rows deterministically, preserving totals,
        // identities, and parent-before-child order. Never cut serialized JSON text.
        var plcIndex = report.Plcs.Count - 1;
        while (JsonSerializer.Serialize(report, ReportJson).Length >= MaxSerializedCharacters)
        {
            while (plcIndex >= 0 && report.Plcs[plcIndex].Messages.Count == 0)
                plcIndex--;

            if (plcIndex < 0)
                throw new InvalidOperationException("Compile report metadata exceeds the response limit. Compilation may have run; inspect the current in-memory project state before deciding whether to retry.");

            var plc = report.Plcs[plcIndex];
            plc.Messages.RemoveAt(plc.Messages.Count - 1);
            NoteOmission(plc);
        }
    }
}
