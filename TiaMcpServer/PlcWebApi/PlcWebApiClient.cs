using System.Globalization;
using System.Text.Json;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.PlcWebApi;

/// <summary>
/// The PLC's own error evidence for <c>webapi_rejected</c> (and, when present, the other Web API
/// categories): the JSON-RPC error code and name, the message the PLC returned, and the HTTP status.
/// </summary>
public sealed record PlcWebApiErrorInfo(int? Code, string? Name, string? PlcMessage, int? HttpStatus);

/// <summary>
/// The only exception an <see cref="IPlcWebApiClient"/> throws besides caller cancellation. Its
/// message never contains credentials, tokens or the raw request text.
/// </summary>
public sealed class PlcWebApiException : Exception
{
    public PlcWebApiException(string category, string message, PlcWebApiErrorInfo? error = null)
        : base(message)
    {
        if (!WorkerFailureCategories.IsKnown(category))
            throw new ArgumentException($"'{category}' is not an approved failure category.", nameof(category));
        Category = category;
        Error = error;
    }

    public string Category { get; }

    public PlcWebApiErrorInfo? Error { get; }
}

/// <summary>
/// One target's Web API endpoint, Siemens-free so every caller can be tested with a fake. The
/// production implementation wraps the official <c>Siemens.Simatic.S7.Webserver.API</c> client.
/// Every method throws <see cref="PlcWebApiException"/> or <see cref="OperationCanceledException"/>
/// (only for the caller's own cancellation).
/// </summary>
public interface IPlcWebApiClient : IDisposable
{
    /// <summary>True after a successful <see cref="LoginAsync"/> until <see cref="LogoutAsync"/> or <see cref="DropSession"/>.</summary>
    bool HasSession { get; }

    Task LoginAsync(string userName, string password, CancellationToken cancellationToken);

    /// <summary>Api.Logout; the session token is dropped even when the PLC refuses.</summary>
    Task LogoutAsync(CancellationToken cancellationToken);

    /// <summary>Forgets the session token locally without contacting the PLC.</summary>
    void DropSession();

    Task<string> PingAsync(CancellationToken cancellationToken);

    Task<double> VersionAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetPermissionsAsync(CancellationToken cancellationToken);

    Task<string> ReadOperatingModeAsync(CancellationToken cancellationToken);

    Task<string> ReadModeSelectorStateAsync(CancellationToken cancellationToken);

    Task<PlcWebApiCpuTypeInfo> ReadCpuTypeAsync(CancellationToken cancellationToken);

    Task<PlcWebApiRuntimeInfo> ReadRuntimeInformationAsync(CancellationToken cancellationToken);

    Task<DateTime> ReadSystemTimeAsync(CancellationToken cancellationToken);

    Task<PlcWebApiProjectInfo> ReadProjectInformationAsync(CancellationToken cancellationToken);

    /// <param name="mode"><c>children</c> or <c>var</c>.</param>
    Task<IReadOnlyList<PlcWebApiVariableInfo>> BrowseVariablesAsync(string mode, string? variable, CancellationToken cancellationToken);

    Task<IReadOnlyList<PlcWebApiCodeBlockInfo>> BrowseCodeBlocksAsync(CancellationToken cancellationToken);

    /// <summary>PlcProgram.Read of one symbolic variable; the value is the PLC's JSON result, unconverted.</summary>
    Task<JsonElement> ReadVariableAsync(string variable, string? representation, CancellationToken cancellationToken);

    Task<bool> WriteVariableAsync(string variable, JsonElement value, string? representation, CancellationToken cancellationToken);

    Task<PlcWebApiDiagnosticBufferInfo> BrowseDiagnosticBufferAsync(string language, int? count, CancellationToken cancellationToken);

    Task<PlcWebApiAlarmsInfo> BrowseAlarmsAsync(string language, int? count, string? alarmId, CancellationToken cancellationToken);

    Task<bool> AcknowledgeAlarmAsync(string alarmId, CancellationToken cancellationToken);

    /// <param name="mode"><c>children</c>, <c>node</c> or null.</param>
    Task<IReadOnlyList<PlcWebApiModuleInfo>> BrowseModulesAsync(uint? hwid, string? mode, CancellationToken cancellationToken);

    Task<PlcWebApiModuleStatusInfo> ReadModuleStatusAsync(uint hwid, string? language, CancellationToken cancellationToken);

    Task<IReadOnlyList<PlcWebApiLedInfo>> ReadModuleLedsAsync(uint hwid, CancellationToken cancellationToken);

    Task<bool> FlashLedsAsync(uint hwid, CancellationToken cancellationToken);

    Task<PlcWebApiSyslogInfo> BrowseSyslogAsync(int? count, int? first, CancellationToken cancellationToken);

    /// <param name="mode"><c>run</c> or <c>stop</c>.</param>
    Task<bool> RequestChangeOperatingModeAsync(string mode, CancellationToken cancellationToken);

    Task<bool> SetSystemTimeAsync(DateTime utcTimestamp, CancellationToken cancellationToken);

    /// <summary>Plc.CreateBackup, one request; returns the ticket the backup is downloaded with.</summary>
    Task<string> RequestBackupAsync(CancellationToken cancellationToken);

    /// <summary>Downloads the backup ticket's content and closes the ticket (best effort); returns the backup bytes.</summary>
    Task<byte[]> DownloadBackupAsync(string ticket, CancellationToken cancellationToken);

    /// <summary>The client's backup-restore helper: uploads the file, waits for the PLC to restart and logs in again.</summary>
    Task RestoreBackupAsync(string filePath, string userName, string password, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>Creates one client per configured target; each owns its own HTTP connection and certificate policy.</summary>
public interface IPlcWebApiClientFactory
{
    IPlcWebApiClient Create(PlcWebApiTarget target);
}

public sealed record PlcWebApiCpuTypeInfo(string? ProductName, string? OrderNumber);

public sealed record PlcWebApiRuntimeInfo(
    double? ProgramLoadCyclicProgramObsPercentage,
    double? ProgramLoadHighPriorityObsPercentage,
    double? CurrentCommunicationLoadPercentage,
    double? MaxCommunicationLoadPercentage,
    double? CycleTimeShortestMs,
    double? CycleTimeCurrentMs,
    double? CycleTimeLongestMs,
    double? CycleTimeConfiguredMinMs,
    double? CycleTimeConfiguredMaxMs);

public sealed record PlcWebApiProjectVersionInfo(string? Source, string? Version);

public sealed record PlcWebApiProjectInfo(string? ProjectName, IReadOnlyList<PlcWebApiProjectVersionInfo> Versions);

public sealed record PlcWebApiArrayDimensionInfo(int StartIndex, int Count);

public sealed record PlcWebApiVariableInfo(
    string? Name,
    string? Datatype,
    bool? HasChildren,
    int? DbNumber,
    string? Address,
    string? Area,
    bool? ReadOnly,
    int? MaxLength,
    IReadOnlyList<PlcWebApiArrayDimensionInfo> ArrayDimensions);

public sealed record PlcWebApiCodeBlockInfo(string? Name, int BlockNumber, string? BlockType);

public sealed record PlcWebApiDiagnosticEntryInfo(
    string Timestamp,
    string? Status,
    string? ShortText,
    string? LongText,
    string? HelpText,
    int? TextlistId,
    int? TextId);

public sealed record PlcWebApiDiagnosticBufferInfo(
    string? Language,
    string? LastModified,
    int CountCurrent,
    int CountMax,
    IReadOnlyList<PlcWebApiDiagnosticEntryInfo> Entries);

public sealed record PlcWebApiAlarmEntryInfo(
    string? Id,
    int? AlarmNumber,
    string? Status,
    string Timestamp,
    string? Producer,
    int? Hwid,
    string? AcknowledgementState,
    string? AcknowledgementTimestamp,
    string? AlarmText,
    string? InfoText,
    bool? TextInconsistent);

public sealed record PlcWebApiAlarmsInfo(
    string? Language,
    string? LastModified,
    long CountCurrent,
    long CountMax,
    IReadOnlyList<PlcWebApiAlarmEntryInfo> Entries);

public sealed record PlcWebApiModuleInfo(
    long Hwid,
    string? Name,
    string? Class,
    string? Type,
    string? SubType,
    IReadOnlyList<string> Attributes,
    bool HasChildren,
    IReadOnlyList<long> Parent,
    IReadOnlyList<PlcWebApiModuleInfo> Children);

public sealed record PlcWebApiModuleStatusInfo(string? Own, string? Subordinate, IReadOnlyList<string> Messages, string? Language);

public sealed record PlcWebApiLedInfo(string? Type, string? Status, IReadOnlyList<string> Colors, double? PeriodMs);

public sealed record PlcWebApiSyslogInfo(long CountTotal, long CountLost, IReadOnlyList<string> Entries);

/// <summary>Formatting shared by the client adapter and the tools.</summary>
public static class PlcWebApiFormat
{
    /// <summary>A UTC timestamp as ISO 8601 with Z; a timestamp of unspecified kind is written without a zone.</summary>
    public static string? Timestamp(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } utc => utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture),
        { Kind: DateTimeKind.Local } local => local.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture),
        { } unspecified => unspecified.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture),
    };
}
