namespace TiaMcpServer.OpennessWorker.Openness.Block;

/// <summary>
/// The messages of a <c>DocumentImportResultForBlocks</c>. The Siemens V21 skill simatic-sd §3 reads them as
/// <c>result.Messages.Select(m => m.Message)</c>, but neither member is in the compile stubs (their types are not
/// documented), so both are bound at runtime by exact name; an unreadable member yields a note instead of a message,
/// never a failure of its own (the import already failed). Siemens-free: works on any object.
/// </summary>
internal static class SimaticSdImportMessages
{
    public static IReadOnlyList<string> Read(object? result)
    {
        if (result is null) return Array.Empty<string>();
        object? messages;
        try
        {
            messages = result.GetType().GetProperty("Messages")?.GetValue(result);
        }
        catch (Exception exception) when (exception is System.Reflection.TargetInvocationException or InvalidOperationException)
        {
            return new[] { "the result's Messages could not be read (" + exception.GetBaseException().Message + ")" };
        }

        if (messages is not System.Collections.IEnumerable items)
            return new[] { "the result exposes no readable Messages member (bound at runtime; not in the compile stubs)" };
        var texts = new List<string>();
        foreach (var item in items)
        {
            if (item is null) continue;
            string? text;
            try
            {
                text = item.GetType().GetProperty("Message")?.GetValue(item)?.ToString();
            }
            catch (Exception exception) when (exception is System.Reflection.TargetInvocationException or InvalidOperationException)
            {
                text = null;
            }

            texts.Add(string.IsNullOrWhiteSpace(text) ? item.ToString() ?? "(unreadable message)" : text!);
            if (texts.Count >= 20) break;
        }

        return texts;
    }

    /// <summary>One controlled warning per message (collapsed to one line), or one naming the state when there is none.</summary>
    public static IReadOnlyList<string> AsWarnings(IReadOnlyList<string> messages, string state)
        => (messages.Count == 0 ? new[] { $"TIA Portal returned state '{state}' without messages." } : messages)
            .Select(m => BlockImportDiagnosticSanitizer.SimaticSdMessagePrefix + string.Join(" ", m.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim())
            .ToList();
}
