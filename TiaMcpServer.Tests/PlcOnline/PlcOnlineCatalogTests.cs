using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Plc;
using Xunit;

namespace TiaMcpServer.Tests.PlcOnline;

/// <summary>
/// Capability classification, container delivery and plc_read/plc_write catalog validation of the compare,
/// online, station-upload, watch/force table and update_program operations.
/// </summary>
public sealed class PlcOnlineCatalogTests
{
    [Fact]
    public void OnlineObserve_IsAppendedLastAndAllowedInEveryMode()
    {
        Assert.Equal(8, (int)OperationCapability.OnlineObserve);
        Assert.Equal(OperationCapability.OnlineObserve, Enum.GetValues<OperationCapability>().Max());
        foreach (var mode in new[] { McpAccessMode.ReadOnly, McpAccessMode.ReadWrite, McpAccessMode.Full })
            Assert.True(OperationPolicyCatalog.IsCapabilityAllowed(mode, OperationCapability.OnlineObserve));
        Assert.Equal(new[] { PlcOnlineMethods.CompareToOnline, PlcOnlineMethods.CompareHardwareToOnline, TiaMcpServer.Contracts.Plc.PlcSourceFolderMethods.ScanAccessibleDevices,
                PlcOnlineMethods.GetStationFingerprintData /* audit N3 */ }
            .Concat(TiaMcpServer.Contracts.Plc.Settings.PlcUmacMethods.UmcServerReads) // package G: the UMC server reads
            .OrderBy(name => name, StringComparer.Ordinal), OperationPolicyCatalog.AllOperationNames
            .Where(name => OperationPolicyCatalog.GetCapability(name) == OperationCapability.OnlineObserve).OrderBy(name => name, StringComparer.Ordinal));
        Assert.True(OperationPolicyCatalog.RequiresExpectedSessionIdentity(PlcOnlineMethods.CompareHardwareToOnline));
        Assert.True(OperationPolicyCatalog.RequiresExpectedSessionIdentity(PlcOnlineMethods.CompareToOnline));
    }

    [Theory]
    [InlineData(PlcOnlineMethods.CompareSoftware, OperationCapability.Observe)]
    [InlineData(PlcOnlineMethods.GetOnlineState, OperationCapability.Observe)]
    [InlineData(PlcOnlineMethods.ListStationUploadInterfaces, OperationCapability.Observe)]
    [InlineData(PlcOnlineMethods.ListWatchAndForceTables, OperationCapability.Observe)]
    [InlineData(PlcOnlineMethods.ReadWatchTable, OperationCapability.Observe)]
    [InlineData(PlcOnlineMethods.ReadForceTable, OperationCapability.Observe)]
    [InlineData(PlcOnlineMethods.GetWatchTableContent, OperationCapability.TemporaryExport)]
    [InlineData(PlcOnlineMethods.GetForceTableContent, OperationCapability.TemporaryExport)]
    [InlineData(PlcOnlineMethods.CompareToOnline, OperationCapability.OnlineObserve)]
    [InlineData(PlcOnlineMethods.CompareHardware, OperationCapability.Observe)]
    [InlineData(PlcOnlineMethods.CompareHardwareToOnline, OperationCapability.OnlineObserve)]
    [InlineData(PlcOnlineMethods.CreateWatchTable, OperationCapability.ProjectMutation)]
    [InlineData(PlcOnlineMethods.DeleteWatchTable, OperationCapability.ProjectMutation)]
    [InlineData(PlcOnlineMethods.CreateWatchTableGroup, OperationCapability.ProjectMutation)]
    [InlineData(PlcOnlineMethods.DeleteWatchTableGroup, OperationCapability.ProjectMutation)]
    [InlineData(PlcOnlineMethods.UpdateWatchTableContent, OperationCapability.ProjectMutation)]
    [InlineData(PlcOnlineMethods.UpdateForceTableContent, OperationCapability.ProjectMutation)]
    [InlineData(PlcOnlineMethods.UpdateProgram, OperationCapability.ProjectMutation)]
    [InlineData(PlcOnlineMethods.StationUpload, OperationCapability.ProjectMutation)]
    [InlineData(PlcOnlineMethods.GoOnline, OperationCapability.OnlineControl)]
    [InlineData(PlcOnlineMethods.GoOffline, OperationCapability.OnlineControl)]
    public void EveryMethod_HasItsCapability(string method, OperationCapability capability)
    {
        Assert.Equal(capability, OperationPolicyCatalog.GetCapability(method));
        var readOnly = capability is OperationCapability.Observe or OperationCapability.TemporaryExport or OperationCapability.OnlineObserve;
        Assert.Equal(readOnly, OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, method));
        Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadWrite, method));
        Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.Full, method));
        Assert.True(ProjectCapabilityCatalog.RequiresBoundProject(method));
    }

    [Fact]
    public void OnlineToolMethods_MapToTheToolAndAreStandaloneOnly()
    {
        Assert.Equal(PlcOnlineMethods.OnlineTool, ProjectCapabilityCatalog.OperationFor(PlcOnlineMethods.GoOnline));
        Assert.Equal(PlcOnlineMethods.OnlineTool, ProjectCapabilityCatalog.OperationFor(PlcOnlineMethods.GoOffline));
        foreach (var method in PlcOnlineMethods.StandaloneOnlyOperations.Append(PlcOnlineMethods.GoOnline).Append(PlcOnlineMethods.GoOffline))
        {
            Assert.True(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.StandaloneProject, method), method);
            Assert.False(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.LocalSession, method), method);
        }
    }

    [Fact]
    public void WatchTablesAndUpdateProgram_AreProjectContentForLocalSessionsToo()
    {
        foreach (var method in PlcOnlineMethods.ContentReadMethods.Concat(PlcOnlineMethods.ContentWriteMethods))
        {
            Assert.True(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.StandaloneProject, method), method);
            Assert.True(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.LocalSession, method), method);
        }
    }

    private static PlcOperationRequest Read(string operation) => new() { OperationId = "a", Operation = operation };

    [Fact]
    public void ReadCatalog_AcceptsTheNewOperations()
    {
        var operations = new[]
        {
            Read(PlcOnlineMethods.CompareToOnline),
            new PlcOperationRequest { OperationId = "b", Operation = PlcOnlineMethods.CompareSoftware, CompareTarget = "plc", TargetName = "PLC_2", MaxDepth = 4, MaxElements = 50, DifferencesOnly = false },
            new PlcOperationRequest { OperationId = "c", Operation = PlcOnlineMethods.CompareSoftware, CompareTarget = "project_library" },
            new PlcOperationRequest { OperationId = "d", Operation = PlcOnlineMethods.GetOnlineState, PlcName = "PLC_1" },
            new PlcOperationRequest { OperationId = "e", Operation = PlcOnlineMethods.ListStationUploadInterfaces },
            new PlcOperationRequest { OperationId = "f", Operation = PlcOnlineMethods.ListWatchAndForceTables },
            new PlcOperationRequest { OperationId = "g", Operation = PlcOnlineMethods.ReadWatchTable, TableName = "Watch_1", FolderPath = "/Line", MaxElements = 10 },
            new PlcOperationRequest { OperationId = "h", Operation = PlcOnlineMethods.GetForceTableContent, TableName = "Force_1" },
        };

        Assert.True(PlcOperationCatalog.ValidateRead(operations).IsValid);
    }

    [Theory]
    [InlineData(PlcOnlineMethods.CompareSoftware, null, null, null, "missing required field(s): compareTarget")]
    [InlineData(PlcOnlineMethods.CompareSoftware, "master_copy", null, null, "requires a nonblank targetName")]
    [InlineData(PlcOnlineMethods.CompareSoftware, "global_library", null, null, "compareTarget must be one of")]
    [InlineData(PlcOnlineMethods.CompareToOnline, "plc", null, null, "'compareTarget' is not valid")]
    [InlineData(PlcOnlineMethods.CompareToOnline, null, null, 0, "maxElements must be between 1")]
    [InlineData(PlcOnlineMethods.ReadWatchTable, null, "Watch_1", 6000, "maxElements must be between 1 and 5000")]
    [InlineData(PlcOnlineMethods.GetWatchTableContent, null, "Watch_1", 5, "'maxElements' is not valid")]
    [InlineData(PlcOnlineMethods.ReadForceTable, null, null, null, "missing required field(s): tableName")]
    public void ReadCatalog_RejectsMalformedItems(string operation, string? target, string? table, int? max, string expected)
    {
        var validation = PlcOperationCatalog.ValidateRead(new[]
        {
            new PlcOperationRequest { OperationId = "a", Operation = operation, CompareTarget = target, TableName = table, MaxElements = max },
        });

        Assert.False(validation.IsValid);
        Assert.Contains(expected, validation.Error);
    }

    [Fact]
    public void WriteCatalog_ValidatesTheNewOperations()
    {
        Assert.True(PlcOperationCatalog.ValidateWrite(new[]
        {
            new PlcOperationRequest { OperationId = "a", Operation = PlcOnlineMethods.CreateWatchTableGroup, FolderPath = "/Line" },
            new PlcOperationRequest { OperationId = "b", Operation = PlcOnlineMethods.CreateWatchTable, TableName = "W", FolderPath = "/Line" },
            new PlcOperationRequest { OperationId = "c", Operation = PlcOnlineMethods.UpdateProgram, PlcName = "PLC_1" },
            new PlcOperationRequest
            {
                OperationId = "d", Operation = PlcOnlineMethods.UpdateForceTableContent, TableName = "Force_1", Content = "<Document/>",
                ExpectedContentHash = "xml:sha256:" + new string('a', 64),
            },
        }).IsValid);

        var root = PlcOperationCatalog.ValidateWrite(new[] { new PlcOperationRequest { OperationId = "a", Operation = PlcOnlineMethods.DeleteWatchTableGroup, FolderPath = "/" } });
        Assert.Contains("system group '/' cannot be created or deleted", root.Error);
        var sourceHash = PlcOperationCatalog.ValidateWrite(new[]
        {
            new PlcOperationRequest
            {
                OperationId = "a", Operation = PlcOnlineMethods.UpdateWatchTableContent, TableName = "W", Content = "<Document/>",
                ExpectedContentHash = "source:sha256:" + new string('a', 64),
            },
        });
        Assert.Contains("was taken from the 'source' format", sourceHash.Error);
        Assert.Equal(WorkerFailureCategories.ValidationError, sourceHash.Category);
    }

    [Fact]
    public void EveryNewPlcOperation_IsInThePolicyCatalogAndItsMethodList()
    {
        var readOps = PlcOperationCatalog.ReadOperationNames.Where(name => PlcOnlineMethods.ObserveMethods.Concat(PlcOnlineMethods.TemporaryExportMethods)
            .Concat(PlcOnlineMethods.OnlineObserveMethods).Contains(name)).ToArray();
        Assert.Equal(12, readOps.Length); // 11 + get_station_fingerprint_data (audit N3)
        Assert.Equal(PlcOnlineMethods.ContentWriteMethods.OrderBy(n => n), PlcOperationCatalog.WriteOperationNames
            .Where(name => PlcOnlineMethods.ProjectMutationMethods.Contains(name)).OrderBy(n => n));
    }
}
