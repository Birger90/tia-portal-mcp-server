using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TiaMcpServer.Contracts.Plc;

/// <summary>What a recovery folder's <c>recovery.json</c> sidecar records about the kept export.</summary>
public sealed class PlcRecoveryRecord
{
    public int FormatVersion { get; set; } = 1;

    public DateTime CreatedUtc { get; set; }

    public string Operation { get; set; } = string.Empty;

    /// <summary><c>block</c> or <c>tagTable</c>.</summary>
    public string ObjectKind { get; set; } = string.Empty;

    /// <summary>The original object name; the document carries it too.</summary>
    public string ObjectName { get; set; } = string.Empty;

    public string? PlcName { get; set; }

    public string SourcePath { get; set; } = string.Empty;

    public string? TargetPath { get; set; }

    public string Failure { get; set; } = string.Empty;

    public string DocumentFile { get; set; } = PlcRecoveryStore.DocumentFileName;
}

public sealed class PlcRecoverySaveResult
{
    /// <summary>The written folder; null when the document could not be written anywhere.</summary>
    public PlcRecoveryFileInfo? File { get; set; }

    /// <summary>Every failed attempt; null when the first location took both files.</summary>
    public string? Error { get; set; }
}

/// <summary>
/// Keeps the SimaticML export of an object that a failed move removed from the project. Each save
/// writes a new folder <c>&lt;UTC timestamp&gt;-&lt;guid&gt;</c> under
/// <c>%LOCALAPPDATA%\TiaMcpServer\recovery</c> (beside the audit folder; the system temp folder only
/// when that one is not writable) holding <c>document.xml</c> and the <c>recovery.json</c> sidecar.
/// File names are fixed, so the object name never becomes part of a path. Nothing here deletes a
/// written file, and every non-fatal failure is reported instead of thrown.
/// </summary>
public static class PlcRecoveryStore
{
    public const string DocumentFileName = "document.xml";

    public const string MetadataFileName = "recovery.json";

    public const string LocalAppDataLocation = "localAppData";

    public const string TempLocation = "temp";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TiaMcpServer", "recovery");

    public static string FallbackRoot => Path.Combine(Path.GetTempPath(), "TiaMcpServer", "recovery");

    /// <summary>The default locations in order: the LocalAppData folder, then the temp folder.</summary>
    public static IReadOnlyList<(string Root, string Location)> DefaultRoots => new[]
    {
        (DefaultRoot, LocalAppDataLocation),
        (FallbackRoot, TempLocation),
    };

    public static PlcRecoverySaveResult Save(PlcRecoveryRecord record, string document,
        IReadOnlyList<(string Root, string Location)>? roots = null, DateTime? utcNow = null)
    {
        if (record is null) throw new ArgumentNullException(nameof(record));
        var now = (utcNow ?? DateTime.UtcNow).ToUniversalTime();
        record.CreatedUtc = now;
        record.DocumentFile = DocumentFileName;
        var errors = new List<string>();
        foreach (var (root, location) in roots ?? DefaultRoots)
        {
            string? directory = null;
            try
            {
                directory = Path.Combine(root, now.ToString("yyyyMMdd'T'HHmmssfff'Z'", System.Globalization.CultureInfo.InvariantCulture)
                    + "-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                var documentPath = Path.Combine(directory, DocumentFileName);
                System.IO.File.WriteAllText(documentPath, document, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                var file = new PlcRecoveryFileInfo { Directory = directory, DocumentPath = documentPath, Location = location };
                try
                {
                    var metadataPath = Path.Combine(directory, MetadataFileName);
                    System.IO.File.WriteAllText(metadataPath, JsonSerializer.Serialize(record, Options), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    file.MetadataPath = metadataPath;
                }
                catch (Exception exception) when (!IsFatal(exception))
                {
                    errors.Add($"{location}: the document was written but its sidecar was not ({exception.GetType().Name}: {exception.Message})");
                }

                return new PlcRecoverySaveResult { File = file, Error = errors.Count == 0 ? null : string.Join("; ", errors) };
            }
            catch (Exception exception) when (!IsFatal(exception))
            {
                errors.Add($"{location} '{directory ?? root}': {exception.GetType().Name}: {exception.Message}");
            }
        }

        return new PlcRecoverySaveResult { Error = string.Join("; ", errors) };
    }

    /// <summary>Exceptions a recovery path must not swallow.</summary>
    public static bool IsFatal(Exception exception)
        => exception is OutOfMemoryException or AccessViolationException or System.Threading.ThreadAbortException;
}
