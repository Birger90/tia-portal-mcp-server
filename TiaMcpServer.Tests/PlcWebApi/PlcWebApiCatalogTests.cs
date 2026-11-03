using System.Text.Json;
using TiaMcpServer.Contracts.PlcWebApi;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.PlcWebApi;
using Xunit;

namespace TiaMcpServer.Tests.PlcWebApi;

public sealed class PlcWebApiCatalogTests
{
    private static PlcOnlineReadRequest Read(string operation, string? target = "Line1") => new() { OperationId = "r", Operation = operation, Target = target };

    private static PlcOnlineWriteRequest Write(string operation, string? target = "Line1") => new() { OperationId = "w", Operation = operation, Target = target };

    private static string Error(TiaMcpServer.OperationBatches.OperationValidation validation)
    {
        Assert.False(validation.IsValid);
        Assert.Equal("validation_error", validation.Category);
        return validation.Error;
    }

    [Fact]
    public void OperationNames_MapExactlyToTheClassifiedWebApiMethods()
    {
        Assert.Equal(PlcWebApiMethods.Read.Order(StringComparer.Ordinal),
            PlcWebApiCatalog.ReadOperationNames.Select(PlcWebApiCatalog.Method).Order(StringComparer.Ordinal));
        Assert.Equal(PlcWebApiMethods.Control.Order(StringComparer.Ordinal),
            PlcWebApiCatalog.WriteOperationNames.Select(PlcWebApiCatalog.Method).Order(StringComparer.Ordinal));
        Assert.Equal(18, PlcWebApiCatalog.ReadOperationNames.Count);
        Assert.Equal(7, PlcWebApiCatalog.WriteOperationNames.Count);
        foreach (var method in PlcWebApiMethods.All)
            Assert.True(PlcWebApiMethods.IsWebApiMethod(method));
        Assert.False(PlcWebApiMethods.IsWebApiMethod("plc_download"));
    }

    [Fact]
    public void AccessMode_ReadsInEveryMode_WritesOnlyInWritableModes()
    {
        var reads = PlcWebApiCatalog.ReadOperationNames.Select(name => (TiaMcpServer.OperationBatches.IOperationBatchItem)Read(name)).ToArray();
        var writes = PlcWebApiCatalog.WriteOperationNames.Select(name => (TiaMcpServer.OperationBatches.IOperationBatchItem)Write(name)).ToArray();
        foreach (var mode in new[] { McpAccessMode.ReadOnly, McpAccessMode.ReadWrite, McpAccessMode.Full })
            Assert.Empty(PlcWebApiCatalog.ValidateAccessMode(reads, mode));
        Assert.Equal(7, PlcWebApiCatalog.ValidateAccessMode(writes, McpAccessMode.ReadOnly).Count);
        Assert.Empty(PlcWebApiCatalog.ValidateAccessMode(writes, McpAccessMode.ReadWrite));
        Assert.Empty(PlcWebApiCatalog.ValidateAccessMode(writes, McpAccessMode.Full));
    }

    [Fact]
    public void ReadValidation_SeparatesToolsAndRefusesForeignMembers()
    {
        Assert.Contains("belongs to plc_online_write", Error(PlcWebApiCatalog.ValidateRead(new[] { Read("change_operating_mode") })));
        Assert.Contains("unknown plc_online_read operation 'start'", Error(PlcWebApiCatalog.ValidateRead(new[] { Read("start") })));
        Assert.Contains("'names' is not valid for ping", Error(PlcWebApiCatalog.ValidateRead(new[] { new PlcOnlineReadRequest
            { OperationId = "r", Operation = "ping", Target = "Line1", Names = new() { "x" } } })));
        Assert.Contains("target is required", Error(PlcWebApiCatalog.ValidateRead(new[] { Read("ping", target: null) })));
        Assert.Contains("at least one operation", Error(PlcWebApiCatalog.ValidateRead(Array.Empty<PlcOnlineReadRequest>())));
        Assert.Contains("Duplicate operationId", Error(PlcWebApiCatalog.ValidateRead(new[] { Read("ping"), Read("version") })));
        Assert.True(PlcWebApiCatalog.ValidateRead(PlcWebApiCatalog.ReadOperationNames.Select((name, index) => Valid(name, index)).ToArray()).IsValid);
    }

    private static PlcOnlineReadRequest Valid(string operation, int index)
    {
        var item = new PlcOnlineReadRequest { OperationId = "r" + index, Operation = operation, Target = "Line1" };
        if (operation == "read_tags") item.Names = new() { "\"DB\".x" };
        if (operation is "read_module_status" or "read_module_leds") item.Hwid = 64;
        if (operation == "read_watch_table_live") item.Table = "Watch_1";
        return item;
    }

    [Theory]
    [InlineData("read_tags", "names", "read_tags needs names")]
    [InlineData("read_diagnostic_buffer", "count0", "count must be 1 to 1000")]
    [InlineData("browse_syslog", "count1001", "count must be 1 to 1000")]
    [InlineData("browse_syslog", "first-1", "first must be 0 or more")]
    [InlineData("browse_alarms", "countAndAlarm", "either alarmId or count")]
    [InlineData("browse_alarms", "language", "culture name")]
    [InlineData("read_module_status", "nohwid", "hwid is required")]
    [InlineData("read_module_leds", "hwidrange", "hwid must be 0 to")]
    [InlineData("browse_modules", "browseModeVar", "children or node")]
    [InlineData("browse_tags", "browseModeNode", "children or var")]
    [InlineData("browse_tags", "varModeWithoutVar", "needs var")]
    [InlineData("browse_tags", "typeOther", "type must be code_blocks")]
    [InlineData("browse_tags", "typeWithVar", "takes neither var")]
    [InlineData("read_tags", "representation", "simple or raw")]
    public void ReadValidation_EnforcesPerOperationRules(string operation, string variant, string expected)
    {
        var item = Valid(operation, 0);
        switch (variant)
        {
            case "names": item.Names = new(); break;
            case "count0": item.Count = 0; break;
            case "count1001": item.Count = 1001; break;
            case "first-1": item.First = -1; break;
            case "countAndAlarm": item.Count = 5; item.AlarmId = "1"; break;
            case "language": item.Language = "english please"; break;
            case "nohwid": item.Hwid = null; break;
            case "hwidrange": item.Hwid = (long)uint.MaxValue + 1; break;
            case "browseModeVar": item.BrowseMode = "var"; break;
            case "browseModeNode": item.BrowseMode = "node"; break;
            case "varModeWithoutVar": item.BrowseMode = "var"; break;
            case "typeOther": item.Type = "data_blocks"; break;
            case "typeWithVar": item.Type = "code_blocks"; item.Var = "\"DB\""; break;
            case "representation": item.Representation = "json"; break;
        }

        Assert.Contains(expected, Error(PlcWebApiCatalog.ValidateRead(new[] { item })));
    }

    [Theory]
    [InlineData("change_operating_mode", "operatingMode must be run or stop")]
    [InlineData("write_tags", "write_tags needs tags")]
    [InlineData("acknowledge_alarm", "needs alarmId")]
    [InlineData("flash_leds", "hwid is required")]
    [InlineData("set_system_time", "RFC 3339")]
    [InlineData("create_backup", "absolute local file path")]
    [InlineData("restore_backup", "absolute local file path")]
    public void WriteValidation_RequiresEachOperationsInput(string operation, string expected)
        => Assert.Contains(expected, Error(PlcWebApiCatalog.ValidateWrite(new[] { Write(operation) })));

    [Fact]
    public void WriteValidation_ChecksValuesTimestampsAndPaths()
    {
        var nullValue = Write("write_tags");
        nullValue.Tags = new() { new PlcOnlineTagWrite { Name = "\"DB\".x", Value = JsonSerializer.SerializeToElement<object?>(null) } };
        Assert.Contains("must not be null", Error(PlcWebApiCatalog.ValidateWrite(new[] { nullValue })));

        var huge = Write("write_tags");
        huge.Tags = new() { new PlcOnlineTagWrite { Name = "\"DB\".x", Value = JsonSerializer.SerializeToElement(new string('x', 5000)) } };
        Assert.Contains("exceeds 4096", Error(PlcWebApiCatalog.ValidateWrite(new[] { huge })));

        foreach (var text in new[] { "2026-10-31T12:00:00", "2026-10-31", "yesterday", "1969-12-31T23:59:59Z" })
        {
            var time = Write("set_system_time");
            time.Timestamp = text;
            Error(PlcWebApiCatalog.ValidateWrite(new[] { time }));
        }

        var offset = Write("set_system_time");
        offset.Timestamp = "2026-10-31T14:00:00.5+02:00";
        Assert.True(PlcWebApiCatalog.ValidateWrite(new[] { offset }).IsValid);
        Assert.True(PlcWebApiCatalog.TryParseTimestamp(offset.Timestamp, out var utc));
        Assert.Equal("2026-10-31T12:00:00.500Z", PlcWebApiCatalog.FormatTimestamp(utc));

        var path = Path.Combine(Path.GetTempPath(), "backup.s7backup");
        var first = Write("create_backup");
        first.Path = path;
        var second = new PlcOnlineWriteRequest { OperationId = "w2", Operation = "create_backup", Target = "Lab", Path = path };
        Assert.Contains("write the same file", Error(PlcWebApiCatalog.ValidateWrite(new[] { first, second })));

        var overwriteOnRestore = Write("restore_backup");
        overwriteOnRestore.Path = path;
        overwriteOnRestore.Overwrite = true;
        Assert.Contains("'overwrite' is not valid for restore_backup", Error(PlcWebApiCatalog.ValidateWrite(new[] { overwriteOnRestore })));
        Assert.Contains("belongs to plc_online_read", Error(PlcWebApiCatalog.ValidateWrite(new[] { Write("read_tags") })));
    }

    [Theory]
    [InlineData(@"\\?\C:\backup.s7backup", true)]
    [InlineData(@"\\.\C:\backup.s7backup", true)]
    [InlineData(@"\??\C:\backup.s7backup", true)]
    [InlineData(@"\\?\UNC\server\share\backup.s7backup", true)]
    [InlineData(@"\backup.s7backup", true)]
    [InlineData(@"C:backup.s7backup", true)]
    [InlineData(@"C:\backups\", true)]
    [InlineData("C:\\backups\\line\u202Egpj.s7backup", true)]
    [InlineData("C:\\backups\\line\n1.s7backup", true)]
    [InlineData("//server/share/backup.s7backup", false)]
    [InlineData("/backups/line\n1.s7backup", false)]
    [InlineData("/backups/line\u200B1.s7backup", false)]
    [InlineData("backups/line1.s7backup", false)]
    [InlineData("/backups/", false)]
    public void BackupPaths_RefuseDeviceRelativeAndUnsafeCharacterPaths(string path, bool windows)
        => Assert.False(PlcWebApiCatalog.IsLocalFilePath(path, windows));

    [Theory]
    [InlineData(@"C:\backups\line1.s7backup", true)]
    [InlineData(@"D:/backups/Größe 1.s7backup", true)]
    [InlineData("/var/backups/line1.s7backup", false)]
    // UNC shares are accepted on Windows since 2026-11-01 (owner decision); the shape check runs on every host.
    [InlineData(@"\\server\share\backup.s7backup", true)]
    [InlineData(@"//server/share/backup.s7backup", true)]
    public void BackupPaths_AcceptLocalDriveUncAndRootedPaths(string path, bool windows)
        => Assert.True(PlcWebApiCatalog.IsLocalFilePath(path, windows));

    [Fact]
    public void BackupPaths_InvalidPathIsAValidationErrorBeforeAnyFileSystemAccess()
    {
        foreach (var operation in new[] { "create_backup", "restore_backup" })
        {
            var write = Write(operation);
            write.Path = @"\\?\C:\backup.s7backup";
            Assert.Contains("no device", Error(PlcWebApiCatalog.ValidateWrite(new[] { write })));
        }
    }
}
