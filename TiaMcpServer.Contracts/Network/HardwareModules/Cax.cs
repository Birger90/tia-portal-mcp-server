using TiaMcpServer.Contracts.Shared;

namespace TiaMcpServer.Contracts.Network;

/// <summary>
/// Names of the CAx (AutomationML) members (GitHub issue #9). Evidence: the V21 IntelliSense file Siemens.Engineering.Step7.xml,
/// <c>T:Siemens.Engineering.Cax.CaxProvider</c> ("Service Provider for CAx Export/Import operations") with
/// <c>Export(Device, FileInfo)</c> and <c>Export(ProjectBase, FileInfo)</c> (return <c>TransferResult</c>),
/// <c>Export(Device, FileInfo, FileInfo)</c>, <c>Export(ProjectBase, FileInfo, FileInfo)</c> and <c>Export(Project, FileInfo, FileInfo)</c>
/// (the third parameter is the log file, return <c>System.Boolean</c>), <c>Import(FileInfo, CaxImportOptions)</c> (return
/// <c>TransferResult</c>) and <c>Import(FileInfo, FileInfo, CaxImportOptions)</c> (log file, return <c>System.Boolean</c>);
/// <c>CaxImportOptions</c> (<c>MoveToParkingLot</c>, <c>OverwriteTiaDevice</c>, <c>RetainTiaDevice</c>); <c>TransferResult</c>
/// (<c>State</c>, <c>ErrorCount</c>, <c>WarningCount</c>, <c>Messages</c>), <c>TransferResultMessage</c> (<c>Message</c>,
/// <c>State</c>, <c>DateTime</c>, <c>ErrorCount</c>, <c>WarningCount</c>, <c>Messages</c>) and <c>TransferResultState</c>
/// (<c>Success</c>, <c>Information</c>, <c>Warning</c>, <c>Error</c>). The XML does not say which object offers the provider; it is
/// taken from the project's <c>GetServiceInfos()</c>. TIA Portal help (V20, "CAx-Daten exportieren und importieren"): format
/// AutomationML, extension .aml.
/// </summary>
public static class CaxNames
{
    public const string Provider = "Siemens.Engineering.Cax.CaxProvider";
    public const string ImportOptionsType = "Siemens.Engineering.Cax.CaxImportOptions";
    public const string Extension = ".aml";

    /// <summary>The documented <c>CaxImportOptions</c> members.</summary>
    public static IReadOnlyList<string> ImportOptions { get; } = new[] { "MoveToParkingLot", "OverwriteTiaDevice", "RetainTiaDevice" };

    /// <summary>The documented <c>TransferResultState</c> member that means failure.</summary>
    public const string ErrorState = "Error";

    public const int MaxMessages = 200;

    public const int MaxMessageDepth = 8;

    public static class Scopes
    {
        public const string Project = "project";
        public const string Device = "device";

        public static IReadOnlyList<string> All { get; } = new[] { Project, Device };
    }
}

/// <summary>One <c>TransferResultMessage</c>, its nested messages included (bounded by <see cref="CaxNames.MaxMessages"/>).</summary>
public sealed class CaxMessageInfo
{
    public RuntimeValueInfo Message { get; set; } = new();

    public RuntimeValueInfo State { get; set; } = new();

    public RuntimeValueInfo DateTime { get; set; } = new();

    public List<CaxMessageInfo> Messages { get; set; } = new();
}

/// <summary>A projected <c>TransferResult</c>: the documented properties as text (the XML gives no types) and the message tree.</summary>
public sealed class CaxTransferResultInfo
{
    public RuntimeValueInfo State { get; set; } = new();

    public RuntimeValueInfo ErrorCount { get; set; } = new();

    public RuntimeValueInfo WarningCount { get; set; } = new();

    public List<CaxMessageInfo> Messages { get; set; } = new();

    /// <summary>Messages not listed because <see cref="CaxNames.MaxMessages"/> or <see cref="CaxNames.MaxMessageDepth"/> was reached.</summary>
    public int MessagesOmitted { get; set; }
}

/// <summary>A file the call wrote, re-read after the call.</summary>
public sealed class CaxFileInfo
{
    public string Path { get; set; } = string.Empty;

    public bool Exists { get; set; }

    public long? Length { get; set; }

    /// <summary>Upper-case hexadecimal SHA-256 of the file; null when it does not exist.</summary>
    public string? Sha256 { get; set; }
}

/// <summary>Result of export_cax.</summary>
public sealed class HardwareCaxExportResultInfo
{
    /// <summary><see cref="CaxNames.Scopes"/>.</summary>
    public string Scope { get; set; } = CaxNames.Scopes.Project;

    /// <summary>The exported device for scope device; null for the project.</summary>
    public string? DeviceName { get; set; }

    /// <summary>The documented overload that was called.</summary>
    public string Overload { get; set; } = string.Empty;

    /// <summary>The projected <c>TransferResult</c> of the overloads without a log file; null with a log file.</summary>
    public CaxTransferResultInfo? Transfer { get; set; }

    /// <summary>The <c>Boolean</c> of the overloads with a log file; null without.</summary>
    public bool? Succeeded { get; set; }

    public CaxFileInfo File { get; set; } = new();

    /// <summary>The log file the overload with a log wrote; null without one.</summary>
    public CaxFileInfo? Log { get; set; }

    public List<string> Messages { get; set; } = new();
}

/// <summary>Result of import_cax: the transfer result and the project's device names before and after.</summary>
public sealed class HardwareCaxImportResultInfo
{
    public string FilePath { get; set; } = string.Empty;

    /// <summary>The <c>CaxImportOptions</c> member passed.</summary>
    public string ImportOption { get; set; } = string.Empty;

    public string Overload { get; set; } = string.Empty;

    public CaxTransferResultInfo? Transfer { get; set; }

    public bool? Succeeded { get; set; }

    public CaxFileInfo? Log { get; set; }

    public List<string> DevicesBefore { get; set; } = new();

    public List<string> DevicesAfter { get; set; } = new();

    public List<string> AddedDevices { get; set; } = new();

    public List<string> RemovedDevices { get; set; } = new();

    public List<string> Messages { get; set; } = new();
}

/// <summary>
/// The Siemens-free projection of a <c>TransferResult</c> and the file re-read, shared by the worker and the FakeWorker. Every
/// property is read by its documented name (<see cref="RuntimeServiceValues.ReadProperty"/>); messages are walked depth-first up to
/// <see cref="CaxNames.MaxMessages"/> entries and <see cref="CaxNames.MaxMessageDepth"/> levels, the rest counted.
/// </summary>
public static class CaxRun
{
    public static CaxTransferResultInfo Read(object result, Func<Exception, bool> recoverable)
    {
        var info = new CaxTransferResultInfo
        {
            State = RuntimeServiceValues.ReadProperty(result, "State", recoverable),
            ErrorCount = RuntimeServiceValues.ReadProperty(result, "ErrorCount", recoverable),
            WarningCount = RuntimeServiceValues.ReadProperty(result, "WarningCount", recoverable),
        };
        var budget = CaxNames.MaxMessages;
        var omitted = 0;
        info.Messages = Messages(result, 1, recoverable, ref budget, ref omitted);
        info.MessagesOmitted = omitted;
        return info;
    }

    /// <summary>True when the result's <c>State</c> reads as the documented <c>Error</c> member.</summary>
    public static bool Failed(CaxTransferResultInfo transfer) => transfer.State.Value == CaxNames.ErrorState;

    /// <summary>A short text of the first error messages, for a failure message.</summary>
    public static string Summary(CaxTransferResultInfo transfer)
    {
        var texts = Flatten(transfer.Messages).Where(message => message.State.Value == CaxNames.ErrorState && message.Message.Value is not null)
            .Select(message => message.Message.Value!).Take(5).ToList();
        return $"TransferResult.State {transfer.State.Value ?? transfer.State.State}, ErrorCount {transfer.ErrorCount.Value ?? "?"}, WarningCount {transfer.WarningCount.Value ?? "?"}"
            + (texts.Count == 0 ? "." : ": " + string.Join(" | ", texts));
    }

    public static IEnumerable<CaxMessageInfo> Flatten(IEnumerable<CaxMessageInfo> messages)
        => messages.SelectMany(message => new[] { message }.Concat(Flatten(message.Messages)));

    public static CaxFileInfo File(string path)
    {
        var info = new CaxFileInfo { Path = path, Exists = System.IO.File.Exists(path) };
        if (!info.Exists) return info;
        using var stream = System.IO.File.OpenRead(path);
        info.Length = stream.Length;
        using var sha = System.Security.Cryptography.SHA256.Create();
        info.Sha256 = string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));
        return info;
    }

    /// <summary>Names in <paramref name="after"/> not in <paramref name="before"/> (ordinal, ignoring case like device names), in order.</summary>
    public static List<string> Added(IEnumerable<string> before, IEnumerable<string> after)
    {
        var known = new HashSet<string>(before, StringComparer.OrdinalIgnoreCase);
        return after.Where(name => !known.Contains(name)).ToList();
    }

    private static List<CaxMessageInfo> Messages(object owner, int depth, Func<Exception, bool> recoverable, ref int budget, ref int omitted)
    {
        var list = new List<CaxMessageInfo>();
        var property = Library.LibraryRuntimeBinding.Property(owner.GetType(), "Messages", out _);
        if (property is null) return list;
        object? value;
        try
        {
            value = property.GetValue(owner, null);
        }
        catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException is not null && recoverable(exception.InnerException))
        {
            return list;
        }

        if (value is not System.Collections.IEnumerable enumerable || value is string) return list;
        foreach (var element in enumerable)
        {
            if (element is null) continue;
            if (budget <= 0 || depth > CaxNames.MaxMessageDepth)
            {
                omitted++;
                continue;
            }

            budget--;
            var message = new CaxMessageInfo
            {
                Message = RuntimeServiceValues.ReadProperty(element, "Message", recoverable),
                State = RuntimeServiceValues.ReadProperty(element, "State", recoverable),
                DateTime = RuntimeServiceValues.ReadProperty(element, "DateTime", recoverable),
            };
            message.Messages = Messages(element, depth + 1, recoverable, ref budget, ref omitted);
            list.Add(message);
        }

        return list;
    }
}
