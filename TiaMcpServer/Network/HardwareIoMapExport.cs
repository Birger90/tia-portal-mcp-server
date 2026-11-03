using System.Globalization;
using System.Text;
using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Network;

/// <summary>Typed result of export_io_map; every member is always written.</summary>
public sealed class IoMapExportResultInfo
{
    public string FilePath { get; set; } = string.Empty;

    /// <summary>True when an existing file was replaced (only with overwrite:true).</summary>
    public bool Overwritten { get; set; }

    public string Delimiter { get; set; } = ",";

    /// <summary>True when the caller asked for raw cells (no formula neutralization).</summary>
    public bool RawCells { get; set; }

    /// <summary>Cells prefixed with a single quote because they began with =, +, -, @, a tab or a carriage return.</summary>
    public int NeutralizedCells { get; set; }

    public int DeviceCount { get; set; }

    public int AddressRowCount { get; set; }

    public int ChannelRowCount { get; set; }

    public int TagMatchCount { get; set; }

    public long Bytes { get; set; }

    /// <summary>SHA-256 (lower-case hex) of the written bytes, re-read from disk after the write.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>True when the hardware read reported complete project discovery.</summary>
    public bool DiscoveryComplete { get; set; }

    public List<string> Messages { get; set; } = new();
}

/// <summary>
/// Pure CSV projection of a <c>read_hardware_config</c> result with I/O details. One <c>address</c> row
/// per <c>Address</c> and one <c>channel</c> row per channel and matched tag (a channel without a
/// matched tag has one row with empty tag columns). Values are those of the read, except that a cell
/// beginning with <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, a tab or a carriage return is prefixed with a single
/// quote so spreadsheet programs do not evaluate it as a formula (unless raw cells are requested);
/// RFC 4180 quoting; CRLF line ends.
/// </summary>
public static class HardwareIoMapCsv
{
    public static readonly IReadOnlyList<string> Columns = new[]
    {
        "RowType", "Device", "ItemPath", "Item", "TypeIdentifier", "PositionNumber",
        "AddressIoType", "StartAddress", "Length", "AddressContext", "Controllers",
        "ChannelNumber", "ChannelIoType", "ChannelType", "LogicalAddress",
        "TagName", "TagDataType", "TagLogicalAddress", "TagTable", "TagFolder",
    };

    public sealed record Projection(string Text, int DeviceCount, int AddressRows, int ChannelRows, int TagMatches, int NeutralizedCells);

    /// <summary>
    /// Leading characters a spreadsheet may treat as the start of a formula (OWASP CSV injection), with their
    /// full-width forms (U+FF1D, U+FF0B, U+FF0D, U+FF20) that some spreadsheets normalize.
    /// </summary>
    public static IReadOnlyList<char> FormulaLeadCharacters { get; } = new[] { '=', '+', '-', '@', '\t', '\r', '\uFF1D', '\uFF0B', '\uFF0D', '\uFF20' };

    /// <summary>Leading whitespace a spreadsheet may skip before it evaluates a formula: space, tab, CR, LF, no-break space.</summary>
    public static IReadOnlyList<char> SkippedLeadingWhitespace { get; } = new[] { ' ', '\t', '\r', '\n', '\u00A0' };

    public static Projection Build(HardwareConfigInfo config, string delimiter, bool rawCells = false)
    {
        var builder = new StringBuilder();
        var neutralized = 0;
        Append(builder, Columns, delimiter);
        int addressRows = 0, channelRows = 0, tagMatches = 0;
        foreach (var device in config.Devices)
        {
            foreach (var item in device.Items) Walk(device.Name ?? string.Empty, item, new List<string>());
        }

        return new Projection(builder.ToString(), config.Devices.Count, addressRows, channelRows, tagMatches, neutralized);

        void Append(StringBuilder target, IReadOnlyList<string> fields, string separator)
            => HardwareIoMapCsv.Append(target, fields.Select(field => Neutralize(field, rawCells, ref neutralized)).ToArray(), separator);

        void Walk(string deviceName, DeviceItemInfo item, List<string> path)
        {
            path.Add(item.Name ?? string.Empty);
            var itemColumns = new[]
            {
                deviceName, string.Join("/", path), item.Name ?? string.Empty, item.TypeIdentifier ?? string.Empty, Number(item.PositionNumber),
            };
            if (item.IoDetails is { } details)
            {
                foreach (var address in details.Addresses)
                {
                    Append(builder, new[] { "address" }.Concat(itemColumns).Concat(new[]
                    {
                        address.IoType ?? string.Empty, Number(address.StartAddress), Number(address.Length), address.Context ?? string.Empty,
                        string.Join("|", address.ControllerNames),
                    }).Concat(Enumerable.Repeat(string.Empty, 9)).ToArray(), delimiter);
                    addressRows++;
                }

                foreach (var channel in details.Channels)
                {
                    var channelColumns = new[]
                    {
                        Number(channel.Number), channel.IoType ?? string.Empty, channel.Type ?? string.Empty, channel.LogicalAddress ?? string.Empty,
                    };
                    var prefix = new[] { "channel" }.Concat(itemColumns).Concat(Enumerable.Repeat(string.Empty, 5)).Concat(channelColumns);
                    if (channel.TagMatches.Count == 0)
                    {
                        Append(builder, prefix.Concat(Enumerable.Repeat(string.Empty, 5)).ToArray(), delimiter);
                        channelRows++;
                    }

                    foreach (var tag in channel.TagMatches)
                    {
                        Append(builder, prefix.Concat(new[] { tag.Name, tag.DataType, tag.LogicalAddress, tag.TableName, tag.FolderPath }).ToArray(), delimiter);
                        channelRows++;
                        tagMatches++;
                    }
                }
            }

            foreach (var child in item.Items) Walk(deviceName, child, path);
            path.RemoveAt(path.Count - 1);
        }
    }

    private static string Number(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>Prefixes a single quote to a value a spreadsheet could evaluate as a formula, unless raw cells are requested.</summary>
    internal static string Neutralize(string value, bool rawCells, ref int neutralized)
    {
        if (rawCells || value.Length == 0) return value;
        // A tab or CR is itself a lead; otherwise the first character after leading whitespace decides.
        var first = 0;
        while (first < value.Length && !FormulaLeadCharacters.Contains(value[first]) && SkippedLeadingWhitespace.Contains(value[first])) first++;
        if (first == value.Length || !FormulaLeadCharacters.Contains(value[first])) return value;
        neutralized++;
        return "'" + value;
    }

    private static void Append(StringBuilder builder, IReadOnlyList<string> fields, string delimiter)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0) builder.Append(delimiter);
            builder.Append(Quote(fields[i], delimiter));
        }

        builder.Append("\r\n");
    }

    internal static string Quote(string value, string delimiter)
        => value.Contains(delimiter, StringComparison.Ordinal) || value.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
}

/// <summary>
/// export_io_map: reads the hardware configuration with I/O details (and tag matches unless disabled)
/// through the ordinary read path, writes the CSV next to the target through a temporary file and
/// moves it into place. An existing file is never replaced unless <c>overwrite:true</c>; the written
/// bytes are re-read and hashed.
/// </summary>
public static class HardwareIoMapExport
{
    public static async Task<WorkerCallResult> ExecuteAsync(OpennessWorkerClient client, NetworkOperationRequest operation)
    {
        var path = operation.FilePath!;
        var overwrite = operation.Overwrite == true;
        var delimiter = operation.CsvDelimiter ?? ",";
        var rawCells = operation.RawCells == true;
        if (Precheck(path, overwrite) is { } refusal) return refusal;

        var read = await client.ReadHardwareConfigAsync(deviceName: operation.DeviceName, plcName: operation.PlcName,
            includeIoDetails: true, includeTagMatches: operation.IncludeTagMatches ?? true).ConfigureAwait(false);
        if (!read.Success)
            return WorkerCallResult.Fail(read.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                $"export_io_map: the hardware configuration could not be read: {read.Error} No file was written.", read.Warnings);
        HardwareConfigInfo config;
        try
        {
            config = NetworkPayloadContract.DecodeHardwareConfig(read.Payload);
        }
        catch (JsonException)
        {
            return WorkerCallResult.Fail(WorkerFailureCategories.ProtocolError,
                "export_io_map: the hardware configuration payload did not match its declared result contract and was rejected. No file was written.");
        }

        var projection = HardwareIoMapCsv.Build(config, delimiter, rawCells);
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble()
            .Concat(new UTF8Encoding(false).GetBytes(projection.Text)).ToArray();
        TiaMcpServer.Contracts.Plc.PlcWrittenFile written;
        try
        {
            // The shared writer: temporary file and rename, the previous file kept until the re-read matches.
            written = TiaMcpServer.Contracts.Plc.PlcFileTargets.Write(path, bytes, overwrite);
        }
        catch (TiaMcpServer.Contracts.Plc.PlcFileNotVerifiedException exception)
        {
            return WorkerCallResult.Fail(WorkerFailureCategories.PostconditionFailed, $"export_io_map: {exception.Message}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return WorkerCallResult.Fail(
                !overwrite && File.Exists(path) ? WorkerFailureCategories.ValidationError : WorkerFailureCategories.WorkerOperationFailed,
                $"export_io_map: the file '{path}' could not be written: {exception.Message}"
                + (!overwrite && File.Exists(path) ? " It exists; pass overwrite:true to replace it." : string.Empty));
        }

        var result = new IoMapExportResultInfo
        {
            FilePath = path,
            Overwritten = written.Replaced,
            Delimiter = delimiter,
            RawCells = rawCells,
            NeutralizedCells = projection.NeutralizedCells,
            DeviceCount = projection.DeviceCount,
            AddressRowCount = projection.AddressRows,
            ChannelRowCount = projection.ChannelRows,
            TagMatchCount = projection.TagMatches,
            Bytes = written.Bytes,
            Sha256 = written.Sha256,
            DiscoveryComplete = config.DiscoveryEvidence is { Complete: true, Failures.Count: 0 },
            Messages = config.Messages.ToList(),
        };
        var location = TiaMcpServer.Contracts.Plc.PlcFileTargets.InspectDirectory(Path.GetDirectoryName(path)!).Warning;
        return WorkerCallResult.Ok(WorkerJson.SerializePayload(result), location is null ? read.Warnings : read.Warnings.Append($"export_io_map: {location}").ToList());
    }

    /// <summary>Path checks shared by planning and the write: directory exists, no directory at the path, no overwrite unless asked.</summary>
    internal static WorkerCallResult? Precheck(string path, bool overwrite)
    {
        if (NetworkOperationCatalog.IsDevicePath(path))
            return WorkerCallResult.Fail(WorkerFailureCategories.ValidationError,
                $"export_io_map: '{path}': {TiaMcpServer.Contracts.Files.LocalFilePaths.DevicePathReason} No file was written.");
        if (NetworkOperationCatalog.CheckLocalFile(path) is { } refusal)
            return WorkerCallResult.Fail(WorkerFailureCategories.ValidationError, $"export_io_map: {refusal} No file was written.");
        string? directory;
        try
        {
            directory = Path.GetDirectoryName(path);
        }
        catch (ArgumentException exception)
        {
            return WorkerCallResult.Fail(WorkerFailureCategories.ValidationError, $"export_io_map: 'filePath' is invalid: {exception.Message}");
        }

        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return WorkerCallResult.Fail(WorkerFailureCategories.ValidationError,
                $"export_io_map: the directory of '{path}' does not exist. No file was written.");
        // A mapped network drive, a UNC share and a link to one are allowed (warning on success).
        if (TiaMcpServer.Contracts.Plc.PlcFileTargets.InspectDirectory(directory).Refusal is { } location)
            return WorkerCallResult.Fail(WorkerFailureCategories.ValidationError, $"export_io_map: {location}");
        if (Directory.Exists(path))
            return WorkerCallResult.Fail(WorkerFailureCategories.ValidationError, $"export_io_map: '{path}' is a directory. No file was written.");
        if (File.Exists(path) && !overwrite)
            return WorkerCallResult.Fail(WorkerFailureCategories.ValidationError,
                $"export_io_map: '{path}' already exists; pass overwrite:true to replace it. No file was written.");
        return null;
    }
}
