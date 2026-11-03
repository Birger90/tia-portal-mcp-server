using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Plc;
using TiaMcpServer.PlcOnline;
using TiaMcpServer.ProjectTree;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

/// <summary>Siemens-free rules of the external-source, folder, file-export and accessible-device operations.</summary>
public sealed class PlcSourceFolderUnitTests
{
    // ---------------------------------------------------------------- paths and files

    [Theory]
    [InlineData(@"\\?\C:\a.scl")]
    [InlineData(@"\\.\COM1")]
    [InlineData(@"\??\C:\a.scl")]
    public void DevicePathsAreRefused(string path)
    {
        Assert.True(PlcFileTargets.IsDevicePath(path));
        Assert.False(PlcFileTargets.IsAbsoluteLocalPath(path));
        Assert.Contains("normalization", PlcFileTargets.PathShapeError("filePath", path));
    }

    [Theory]
    [InlineData(@"\\server\share\a.scl")]
    [InlineData("//server/share/a.scl")]
    public void UncSharePaths_AreAccepted(string path)
    {
        // Owner decision 2026-11-01: UNC shares are allowed for every file input and output.
        Assert.True(PlcFileTargets.IsUncShare(path));
        Assert.True(PlcFileTargets.IsAbsoluteLocalPath(path));
        Assert.Null(PlcFileTargets.PathShapeError("filePath", path));
        Assert.Contains("CON", PlcFileTargets.PathShapeError("filePath", @"\\server\share\CON\a.scl"));
    }

    [Theory]
    [InlineData(@"C:\Exports\a.scl", true)]
    [InlineData("C:/Exports/a.scl", true)]
    [InlineData(@"C:relative.scl", false)]
    [InlineData(@"C:\Exports\a.scl:stream", false)]
    [InlineData(@"Exports\a.scl", false)]
    [InlineData("", false)]
    public void WindowsAbsolutePathsAreRecognized(string path, bool absolute)
        => Assert.Equal(absolute, PlcFileTargets.IsAbsoluteLocalPath(path));

    [Fact]
    public void RootedPathsAreAbsoluteOnAForwardSlashPlatform()
        => Assert.Equal(Path.DirectorySeparatorChar == '/', PlcFileTargets.IsAbsoluteLocalPath("/tmp/a.scl"));

    [Theory]
    [InlineData(@"C:\src\Block_1.scl", "Block_1.scl")]
    [InlineData("/tmp/x/UDT.udt", "UDT.udt")]
    [InlineData("Only.db", "Only.db")]
    public void DefaultSourceNameIsTheFileNameWithItsExtension(string path, string name)
        => Assert.Equal(name, PlcFileTargets.DefaultSourceName(path));

    [Theory]
    [InlineData("Main", "Main", false)]
    [InlineData("A/B", "A_B", true)]
    [InlineData("a<b>c:d\"e|f?g*h", "a_b_c_d_e_f_g_h", true)]
    [InlineData("trailing.", "trailing_", true)]
    [InlineData("blank ", "blank_", true)]
    [InlineData("CON", "_CON", true)]
    [InlineData("nul.xml", "_nul.xml", true)]
    [InlineData("COM1x", "COM1x", false)]
    public void SafeFileSegmentFollowsTheWindowsRules(string segment, string expected, bool changed)
        => Assert.Equal((expected, changed), PlcFileTargets.SafeFileSegment(segment));

    [Fact]
    public void WriteGoesThroughATemporaryFileRefusesOverwriteAndHashesTheBytes()
    {
        var directory = Directory.CreateTempSubdirectory("plc-file-").FullName;
        try
        {
            var path = Path.Combine(directory, "out.csv");
            Assert.Null(PlcFileTargets.WriteTargetError(path, overwrite: false));
            var bytes = PlcFileTargets.Utf8WithBom("a,b\r\n");
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
            var first = PlcFileTargets.Write(path, bytes, overwrite: false);
            Assert.False(first.Replaced);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), first.Sha256);
            Assert.Contains("overwrite:true", PlcFileTargets.WriteTargetError(path, overwrite: false));
            Assert.ThrowsAny<IOException>(() => PlcFileTargets.Write(path, Encoding.UTF8.GetBytes("x"), overwrite: false));
            Assert.Equal(bytes, File.ReadAllBytes(path));
            var second = PlcFileTargets.Write(path, Encoding.UTF8.GetBytes("new"), overwrite: true);
            Assert.True(second.Replaced);
            Assert.Equal("new", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(directory));
            Assert.Contains("is a directory", PlcFileTargets.WriteTargetError(directory, overwrite: true));
            Assert.Contains("does not exist", PlcFileTargets.WriteTargetError(Path.Combine(directory, "missing", "x.csv"), overwrite: false));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // ---------------------------------------------------------------- group paths and glob

    [Theory]
    [InlineData("PLC_1/Blocks", "PLC_1", null, "Blocks", "")]
    [InlineData("PLC_1/blocks/Line/Cell", "PLC_1", null, "Blocks", "Line/Cell")]
    [InlineData("PLC_1/Units/U1/Types/Motion", "PLC_1", "U1", "Types", "Motion")]
    [InlineData(" PLC_1 / Types ", "PLC_1", null, "Types", "")]
    public void GroupPathsParse(string path, string plc, string? unit, string kind, string folders)
    {
        Assert.True(PlcGroupPath.TryParse(path, out var group, out var error), error);
        Assert.Equal(plc, group!.PlcName);
        Assert.Equal(unit, group.UnitName);
        Assert.Equal(kind, group.Kind);
        Assert.Equal(folders, string.Join("/", group.Folders));
        Assert.Equal(folders.Length == 0, group.IsRoot);
    }

    [Theory]
    [InlineData("PLC_1")]
    [InlineData("PLC_1/Tags/A")]
    [InlineData("PLC_1//Blocks")]
    [InlineData("PLC_1/Units/U1")]
    [InlineData("")]
    public void MalformedGroupPathsAreRefused(string path)
        => Assert.False(PlcGroupPath.TryParse(path, out _, out _));

    [Fact]
    public void RenamedDisplayPathReplacesTheLastFolder()
    {
        PlcGroupPath.TryParse("PLC_1/Units/U1/Blocks/Line/Cell", out var group, out _);
        Assert.Equal("PLC_1/Units/U1/Blocks/Line/Cell2", group!.RenamedDisplayPath("Cell2"));
    }

    [Theory]
    [InlineData("*", "anything", true)]
    [InlineData("FB_*", "fb_line", true)]
    [InlineData("FB_?ine", "FB_Line", true)]
    [InlineData("*Line*", "FC_LineCell", true)]
    [InlineData("*_DB", "Settings_DB", true)]
    [InlineData("*_DB", "Settings_DBx", false)]
    [InlineData("Main", "Main1", false)]
    [InlineData("a*b*c", "aXbYc", true)]
    [InlineData("a*b*c", "aXbY", false)]
    [InlineData("?", "", false)]
    public void GlobMatchesTheWholeNameCaseInsensitively(string pattern, string text, bool match)
        => Assert.Equal(match, PlcGlob.IsMatch(pattern, text));

    // ---------------------------------------------------------------- catalogs and policy

    [Fact]
    public void CapabilitiesFollowTheAccessModes()
    {
        Assert.Equal(OperationCapability.Observe, OperationPolicyCatalog.GetCapability(PlcSourceFolderMethods.ListExternalSources));
        Assert.Equal(OperationCapability.Observe, OperationPolicyCatalog.GetCapability(PlcSourceFolderMethods.FindObjects));
        Assert.Equal(OperationCapability.OnlineObserve, OperationPolicyCatalog.GetCapability(PlcSourceFolderMethods.ScanAccessibleDevices));
        Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, PlcSourceFolderMethods.ScanAccessibleDevices));
        foreach (var write in PlcSourceFolderMethods.AllWrites)
        {
            Assert.Equal(OperationCapability.ProjectMutation, OperationPolicyCatalog.GetCapability(write));
            Assert.False(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, write));
            Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadWrite, write));
        }
    }

    [Fact]
    public void WorkerMethodsMapToTheirToolsAndTheScanStaysStandalone()
    {
        Assert.Equal("plc_read", ProjectCapabilityCatalog.OperationFor(PlcSourceFolderMethods.ListExternalSources));
        foreach (var write in PlcSourceFolderMethods.WorkerWrites)
            Assert.Equal("plc_write", ProjectCapabilityCatalog.OperationFor(write));
        Assert.Equal(PlcSourceFolderMethods.ScanAccessibleDevices, ProjectCapabilityCatalog.OperationFor(PlcSourceFolderMethods.ScanAccessibleDevices));
        Assert.True(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.StandaloneProject, PlcSourceFolderMethods.ScanAccessibleDevices));
        Assert.False(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.LocalSession, PlcSourceFolderMethods.ScanAccessibleDevices));
        Assert.True(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.LocalSession, PlcSourceFolderMethods.ImportExternalSource));
        Assert.True(OperationPolicyCatalog.RequiresExpectedSessionIdentity(PlcSourceFolderMethods.ScanAccessibleDevices));
        Assert.True(PlcSourceFolderMethods.IsWorkerMethod(PlcSourceFolderMethods.RenameWatchTableGroup));
        Assert.False(PlcSourceFolderMethods.IsWorkerMethod(PlcSourceFolderMethods.ExportBlocks));
        Assert.False(PlcSourceFolderMethods.IsWorkerMethod(PlcSourceFolderMethods.FindObjects));
    }

    [Fact]
    public void CatalogListsEveryOperationOnItsTool()
    {
        foreach (var read in PlcSourceFolderMethods.AllReads) Assert.Contains(read, PlcOperationCatalog.ReadOperationNames);
        foreach (var write in PlcSourceFolderMethods.AllWrites) Assert.Contains(write, PlcOperationCatalog.WriteOperationNames);
    }

    private static PlcOperationRequest Op(string operation, Action<PlcOperationRequest>? set = null)
    {
        var request = new PlcOperationRequest { OperationId = "a", Operation = operation };
        set?.Invoke(request);
        return request;
    }

    private static string? ReadError(PlcOperationRequest op) => PlcOperationCatalog.ValidateRead(new[] { op }) is { IsValid: false } invalid ? invalid.Error : null;

    private static string? WriteError(PlcOperationRequest op) => PlcOperationCatalog.ValidateWrite(new[] { op }) is { IsValid: false } invalid ? invalid.Error : null;

    [Fact]
    public void ValidItemsPass()
    {
        Assert.Null(ReadError(Op(PlcSourceFolderMethods.ListExternalSources)));
        Assert.Null(ReadError(Op(PlcSourceFolderMethods.FindObjects, o => { o.Pattern = "FB_*"; o.Kinds = new[] { "block", "external_source" }; o.Limit = 10; o.Offset = 5; })));
        Assert.Null(ReadError(Op(PlcSourceFolderMethods.ScanAccessibleDevices, o => { o.Mode = "PN/IE"; o.PcInterface = new PlcOnlinePcInterfaceArgument { Name = "Intel", Number = 1 }; })));
        Assert.Null(WriteError(Op(PlcSourceFolderMethods.ImportExternalSource, o => o.FilePath = @"C:\src\Block.scl")));
        Assert.Null(WriteError(Op(PlcSourceFolderMethods.GenerateBlocksFromSource, o => { o.SourceName = "Block.scl"; o.GenerateOption = "KeepOnError"; o.TargetGroupPath = "PLC_1/Types/Motion"; })));
        Assert.Null(WriteError(Op(PlcSourceFolderMethods.ExportSourceFile, o => { o.FilePath = @"C:\out\all.scl"; o.BlockPaths = new[] { "PLC_1/Main" }; o.TypePaths = new[] { "PLC_1/Types/UDT" }; })));
        Assert.Null(WriteError(Op(PlcSourceFolderMethods.RenameBlockGroup, o => { o.BlockPath = "PLC_1/Blocks/Line"; o.NewName = "Line2"; })));
        Assert.Null(WriteError(Op(PlcSourceFolderMethods.RenameTagTableGroup, o => { o.FolderPath = "/Line"; o.NewName = "L2"; o.UnitName = "U1"; })));
        Assert.Null(WriteError(Op(PlcSourceFolderMethods.ExportTagTablesCsv, o => { o.FilePath = @"C:\out\tags.csv"; o.CsvDelimiter = ";"; o.RawCells = true; })));
        Assert.Null(WriteError(Op(PlcSourceFolderMethods.ExportBlocks, o => { o.DirectoryPath = @"C:\out"; o.BlockPath = "PLC_1/Blocks"; o.Format = "source"; o.MaxElements = 2000; })));
    }

    [Fact]
    public void ShapeErrorsAreRefusedBeforeAnyWorkerCall()
    {
        Assert.Contains("pcInterface", ReadError(Op(PlcSourceFolderMethods.ScanAccessibleDevices, o => o.Mode = "PN/IE")));
        Assert.Contains("missing required field(s): mode", ReadError(Op(PlcSourceFolderMethods.ScanAccessibleDevices, o => o.PcInterface = new() { Name = "x", Number = 1 })));
        Assert.Contains("unknown kinds", ReadError(Op(PlcSourceFolderMethods.FindObjects, o => { o.Pattern = "*"; o.Kinds = new[] { "technology_object" }; })));
        Assert.Contains("limit", ReadError(Op(PlcSourceFolderMethods.FindObjects, o => { o.Pattern = "*"; o.Limit = 0; })));
        Assert.Contains("normalization", WriteError(Op(PlcSourceFolderMethods.ImportExternalSource, o => o.FilePath = @"\\?\C:\a.scl")));
        Assert.Null(WriteError(Op(PlcSourceFolderMethods.ImportExternalSource, o => o.FilePath = @"\\server\share\a.scl")));
        Assert.Contains("must end in", WriteError(Op(PlcSourceFolderMethods.ImportExternalSource, o => o.FilePath = @"C:\a.txt")));
        Assert.Contains("for a software unit", WriteError(Op(PlcSourceFolderMethods.ImportExternalSource, o => { o.FilePath = @"C:\a.awl"; o.UnitName = "U1"; })));
        Assert.Contains("generateOption", WriteError(Op(PlcSourceFolderMethods.GenerateBlocksFromSource, o => { o.SourceName = "x"; o.GenerateOption = "keeponerror"; })));
        Assert.Contains("at least one", WriteError(Op(PlcSourceFolderMethods.ExportSourceFile, o => o.FilePath = @"C:\a.scl")));
        Assert.Contains("must not repeat", WriteError(Op(PlcSourceFolderMethods.ExportSourceFile, o => { o.FilePath = @"C:\a.scl"; o.BlockPaths = new[] { "PLC_1/Main", "plc_1/main" }; })));
        Assert.Contains("system group", WriteError(Op(PlcSourceFolderMethods.RenameBlockGroup, o => { o.BlockPath = "PLC_1/Blocks"; o.NewName = "x"; })));
        Assert.Contains("block group path", WriteError(Op(PlcSourceFolderMethods.RenameBlockGroup, o => { o.BlockPath = "PLC_1/Types/A"; o.NewName = "x"; })));
        Assert.Contains("must not contain", WriteError(Op(PlcSourceFolderMethods.RenameTypeGroup, o => { o.TypePath = "PLC_1/Types/A"; o.NewName = "a/b"; })));
        Assert.Contains("system group '/'", WriteError(Op(PlcSourceFolderMethods.RenameWatchTableGroup, o => { o.FolderPath = "/"; o.NewName = "x"; })));
        Assert.Contains(".csv", WriteError(Op(PlcSourceFolderMethods.ExportWatchTablesCsv, o => o.FilePath = @"C:\a.txt")));
        Assert.Contains("csvDelimiter", WriteError(Op(PlcSourceFolderMethods.ExportForceTablesCsv, o => { o.FilePath = @"C:\a.csv"; o.CsvDelimiter = "|"; })));
        Assert.Contains("at most one", WriteError(Op(PlcSourceFolderMethods.ExportBlocks, o => { o.DirectoryPath = @"C:\o"; o.BlockPath = "PLC_1/Blocks"; o.TypePath = "PLC_1/Types"; })));
        Assert.Contains("names PLC", WriteError(Op(PlcSourceFolderMethods.ExportBlocks, o => { o.DirectoryPath = @"C:\o"; o.BlockPath = "PLC_1/Blocks"; o.PlcName = "PLC_2"; })));
        Assert.Contains("maxElements", WriteError(Op(PlcSourceFolderMethods.ExportBlocks, o => { o.DirectoryPath = @"C:\o"; o.MaxElements = 2001; })));
        Assert.Contains("'pattern' is not valid", WriteError(Op(PlcSourceFolderMethods.ExportBlocks, o => { o.DirectoryPath = @"C:\o"; o.Pattern = "*"; })));
        Assert.Contains("is a write operation", ReadError(Op(PlcSourceFolderMethods.ExportBlocks, o => o.DirectoryPath = @"C:\o")));
    }

    [Fact]
    public void RequestCarriesOnlyTheOperationsFields()
    {
        var rename = PlcSourceFolderOperations.Request(Op(PlcSourceFolderMethods.RenameTypeGroup, o => { o.TypePath = "PLC_1/Types/A"; o.NewName = "B"; }));
        Assert.Equal("PLC_1/Types/A", rename.GroupPath);
        Assert.Null(rename.MaxDevices);
        var scan = PlcSourceFolderOperations.Request(Op(PlcSourceFolderMethods.ScanAccessibleDevices, o => { o.Mode = "PN/IE"; o.PcInterface = new() { Name = "PLCSIM", Number = 1 }; }));
        Assert.Equal(("PN/IE", "PLCSIM", 1, PlcAccessibleDeviceProjection.DefaultMaxDevices), (scan.Mode, scan.PcInterfaceName, scan.PcInterfaceNumber, scan.MaxDevices));
        var copy = PlcSourceFolderOperations.Request(Op("copy_block", o => o.TargetGroupPath = "PLC_1/Blocks"));
        Assert.Null(copy.TargetGroupPath);
    }

    // ---------------------------------------------------------------- payload contracts

    [Fact]
    public void ScanResultsThatContradictTheRequestAreRejected()
    {
        var op = Op(PlcSourceFolderMethods.ScanAccessibleDevices, o => { o.Mode = "PN/IE"; o.PcInterface = new() { Name = "Intel", Number = 1 }; });
        string Payload(string mode, int count, bool truncated) => WorkerJson.SerializePayload(new PlcAccessibleDeviceScanInfo
        {
            PlcName = "PLC_1", DeviceName = "S1", Mode = mode, PcInterfaceName = "Intel", PcInterfaceNumber = 1, DeviceCount = count, Truncated = truncated, MaxDevices = 1,
            Devices = new() { new PlcAccessibleDeviceInfo { Name = "a", DeviceSeries = null, Address = "1.2.3.4", MacAddress = null } },
        });
        var ok = PlcSourceFolderOperations.DecodeRead(op, Payload("PN/IE", 2, true));
        Assert.Equal(2, ok.GetProperty("deviceCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, ok.GetProperty("devices")[0].GetProperty("macAddress").ValueKind);
        Assert.Throws<JsonException>(() => PlcSourceFolderOperations.DecodeRead(op, Payload("MPI", 1, false)));
        Assert.Throws<JsonException>(() => PlcSourceFolderOperations.DecodeRead(op, Payload("PN/IE", 2, false)));
    }

    [Fact]
    public void WriteResultsMustMatchTheRequest()
    {
        var rename = Op(PlcSourceFolderMethods.RenameBlockGroup, o => { o.BlockPath = "PLC_1/Blocks/A"; o.NewName = "B"; });
        string Rename(string newName) => WorkerJson.SerializePayload(new PlcGroupRenameInfo
        {
            Operation = PlcSourceFolderMethods.RenameBlockGroup, Kind = "BlockGroup", PlcName = "PLC_1", DeviceName = "S1", UnitName = null,
            Path = "PLC_1/Blocks/A", NewPath = $"PLC_1/Blocks/{newName}", OldName = "A", NewName = newName,
        });
        Assert.Equal("B", PlcSourceFolderOperations.DecodeWrite(rename, Rename("B")).GetProperty("newName").GetString());
        Assert.Throws<JsonException>(() => PlcSourceFolderOperations.DecodeWrite(rename, Rename("A")));

        var export = Op(PlcSourceFolderMethods.ExportSourceFile, o => { o.FilePath = @"C:\a.scl"; o.BlockPaths = new[] { "PLC_1/Main" }; });
        string Export(string sha, bool overwritten) => WorkerJson.SerializePayload(new PlcSourceFileExportInfo
        {
            FilePath = @"C:\a.scl", Overwritten = overwritten, Bytes = 3, Sha256 = sha, WithDependencies = false, PlcName = "PLC_1", UnitName = null, Objects = new() { "PLC_1/Main" },
        });
        var hash = new string('a', 64);
        Assert.Equal(hash, PlcSourceFolderOperations.DecodeWrite(export, Export(hash, false)).GetProperty("sha256").GetString());
        Assert.Throws<JsonException>(() => PlcSourceFolderOperations.DecodeWrite(export, Export("ABC", false)));
        Assert.Throws<JsonException>(() => PlcSourceFolderOperations.DecodeWrite(export, Export(hash, overwritten: true)));
    }

    // ---------------------------------------------------------------- accessible devices

    [Fact]
    public void AccessibleDevicesAreBoundByExactNamesAndFailClosed()
    {
        var pcInterface = new Siemens.Engineering.Connection.ConfigurationPcInterfaceStandIn(new object[]
        {
            new Siemens.Engineering.Connection.ConfigurationAccessibleDevice("plc_1", "S7-1500", "192.168.0.1", "28-63-36-00-00-01"),
            new Siemens.Engineering.Connection.ConfigurationAccessibleDevice("et", null, "192.168.0.2", null),
        });
        var outcome = PlcAccessibleDeviceProjection.Project(PlcAccessibleDeviceProjection.Invoke(pcInterface), maxDevices: 1);
        Assert.Null(outcome.Error);
        Assert.Equal(2, outcome.Count);
        Assert.True(outcome.Truncated);
        var device = Assert.Single(outcome.Devices);
        Assert.Equal(("plc_1", "S7-1500", "192.168.0.1", "28-63-36-00-00-01"), (device.Name, device.DeviceSeries, device.Address, device.MacAddress));

        Assert.Contains("not Siemens.Engineering.Connection.ConfigurationAccessibleDevice",
            PlcAccessibleDeviceProjection.Project(new object[] { "a string" }, 10).Error);
        Assert.Contains("returned null", PlcAccessibleDeviceProjection.Project(null, 10).Error);
        Assert.Contains("not a collection", PlcAccessibleDeviceProjection.Project(42, 10).Error);
        Assert.Contains("only text and enum values",
            PlcAccessibleDeviceProjection.Project(new object[] { new Siemens.Engineering.Connection.ConfigurationAccessibleDevice("a", null, null, null) { AddressOverride = 7 } }, 10).Error);
        Assert.Throws<InvalidOperationException>(() => PlcAccessibleDeviceProjection.Invoke(new object()));
    }

    // ---------------------------------------------------------------- export plan

    private static ProjectTreeObservation Tree()
    {
        static ProjectTreeNode Leaf(string name, string type, string language = "LAD", bool system = false)
            => new() { Name = name, NodeType = type, Children = new(), Details = system ? new() { ["ProgrammingLanguage"] = language, ["IsSystemBlock"] = "true" } : new() { ["ProgrammingLanguage"] = language } };
        static ProjectTreeNode Folder(string name, string type, params ProjectTreeNode[] children) => new() { Name = name, NodeType = type, Children = children.ToList() };
        var software = Folder("PLC_1", ProjectTreeNodeTypes.PlcSoftware,
            Folder("Program blocks", ProjectTreeNodeTypes.BlockFolder,
                Leaf("Main", ProjectTreeNodeTypes.Ob), Leaf("FB_Scl", ProjectTreeNodeTypes.Fb, "SCL"), Leaf("Data", ProjectTreeNodeTypes.GlobalDb, "DB"),
                Folder("Line", ProjectTreeNodeTypes.BlockFolder, Leaf("A:B", ProjectTreeNodeTypes.Fc, "SCL"), Leaf("A_B", ProjectTreeNodeTypes.Fc, "SCL")),
                Folder("System blocks", ProjectTreeNodeTypes.SystemBlockFolder, Leaf("Sys", ProjectTreeNodeTypes.Fb, "STL", system: true))),
            Folder("PLC data types", ProjectTreeNodeTypes.TypeFolder, Leaf("UDT_1", ProjectTreeNodeTypes.Type)),
            Folder("PLC tags", ProjectTreeNodeTypes.TagTableFolder, Leaf("Default tag table", ProjectTreeNodeTypes.TagTable)));
        return new ProjectTreeObservation("p.ap21", null, null, new[] { Folder("Station_1", ProjectTreeNodeTypes.Device, software) }, Array.Empty<ProjectTreeSkippedNodeInfo>(), Array.Empty<string>());
    }

    [Fact]
    public void ExportPlanMirrorsTheTreeExcludesSystemBlocksAndRefusesCollisions()
    {
        var inventory = Assert.Single(PlcScopeInventory.Select(Tree(), null));
        var collide = PlcBlockExportPlan.Build(inventory, Op(PlcSourceFolderMethods.ExportBlocks, o => o.DirectoryPath = "/tmp"));
        Assert.Null(collide.Plan);
        Assert.Contains("Blocks/Line/A_B.xml", collide.Error);

        var (plan, _, error) = PlcBlockExportPlan.Build(inventory, Op(PlcSourceFolderMethods.ExportBlocks, o => { o.DirectoryPath = "/tmp"; o.TypePath = "PLC_1/Types"; }));
        Assert.Null(error);
        Assert.Equal("Types/UDT_1.xml", Assert.Single(plan!.Objects).RelativePrimary());

        var source = PlcBlockExportPlan.Build(inventory, Op(PlcSourceFolderMethods.ExportBlocks, o => { o.DirectoryPath = "/tmp"; o.BlockPath = "PLC_1/Blocks"; o.Format = "source"; o.MaxElements = 100; }));
        Assert.Null(source.Plan);
        var rootOnly = Op(PlcSourceFolderMethods.ExportBlocks, o => { o.DirectoryPath = "/tmp"; o.Format = "source"; o.MaxElements = 3; });
        Assert.Contains("more than maxElements 3", PlcBlockExportPlan.Build(inventory, rootOnly).Error);

        var missing = PlcBlockExportPlan.Build(inventory, Op(PlcSourceFolderMethods.ExportBlocks, o => { o.DirectoryPath = "/tmp"; o.BlockPath = "PLC_1/Blocks/Nope"; }));
        Assert.Equal("target_not_found", missing.Category);
    }

    [Fact]
    public void SourceFormatSkipsIneligibleBlocksByRule()
    {
        var tree = Tree();
        var line = tree.Roots[0].Children![0].Children![0].Children!.Single(c => c.Name == "Line");
        line.Children!.RemoveAt(1);
        var inventory = Assert.Single(PlcScopeInventory.Select(tree, "Station_1"));
        var (plan, _, error) = PlcBlockExportPlan.Build(inventory, Op(PlcSourceFolderMethods.ExportBlocks, o => { o.DirectoryPath = "/tmp"; o.Format = "source"; }));
        Assert.Null(error);
        var byName = plan!.Objects.ToDictionary(o => o.Name);
        Assert.Contains("format source", byName["Main"].SkipReason);
        Assert.Equal(".scl", byName["FB_Scl"].Extension);
        Assert.Equal(".db", byName["Data"].Extension);
        Assert.Equal(".udt", byName["UDT_1"].Extension);
        Assert.Equal("Blocks/Line/A_B.scl", byName["A:B"].RelativePrimary());
        Assert.DoesNotContain("Sys", byName.Keys);
        Assert.Contains(plan.Messages, m => m.Contains("1 system blocks"));
    }

    [Fact]
    public void BundlesSplitIntoTheirDocuments()
    {
        var (documents, error) = PlcFileExports.Documents("--- FILE: Main.xml ---\n<Document/>\n--- FILE: Main.s7res ---\nres", "Main.xml", "xml");
        Assert.Null(error);
        Assert.Equal(new[] { ("Main.xml", "<Document/>\n"), ("Main.s7res", "res") }, documents);
        Assert.Equal(new[] { ("UDT.udt", "TYPE x") }, PlcFileExports.Documents("TYPE x", "UDT.udt", "source").Documents);
    }

    [Fact]
    public void SourceReadsAreWrittenWholeAndXmlBundlesFollowTheImportRules()
    {
        // P5: a source line that looks like a bundle marker is content, never a split.
        const string source = "FUNCTION_BLOCK \"FB\"\n// --- FILE: x ---\n--- FILE: x ---\nBEGIN\nEND_FUNCTION_BLOCK\n";
        Assert.Equal(new[] { ("FB.scl", source) }, PlcFileExports.Documents(source, "FB.scl", "source").Documents);

        Assert.Contains("undeclared content", PlcFileExports.Documents("<Document/>\n--- FILE: Main.s7res ---\nres", "Main.xml", "xml").Error);
        Assert.Contains("malformed", PlcFileExports.Documents("--- FILE: Main.xml ---\n<D/>\n--- FILE:broken\n", "Main.xml", "xml").Error);
        Assert.Contains("twice", PlcFileExports.Documents("--- FILE: a.xml ---\n<D/>\n--- FILE: A.xml ---\n<E/>", "a.xml", "xml").Error);
        Assert.Contains("without content", PlcFileExports.Documents("--- FILE: a.xml ---\n\n--- FILE: b.xml ---\n<E/>", "a.xml", "xml").Error);
        Assert.Equal(new[] { ("Main.xml", "<Document/>") }, PlcFileExports.Documents("<Document/>", "Main.xml", "xml").Documents);
    }

    [Fact]
    public void ExportPlanTellsAnEmptyUnitFromAMissingOne()
    {
        var tree = Tree();
        tree.Roots[0].Children![0].Children!.Add(new ProjectTreeNode { Name = "Empty", NodeType = ProjectTreeNodeTypes.SoftwareUnit, Children = new() });
        var inventory = Assert.Single(PlcScopeInventory.Select(tree, null));
        Assert.Contains("Empty", inventory.Units);
        var (plan, _, error) = PlcBlockExportPlan.Build(inventory, Op(PlcSourceFolderMethods.ExportBlocks, o => { o.DirectoryPath = "/tmp"; o.BlockPath = "PLC_1/Units/Empty/Blocks"; }));
        Assert.Null(error);
        Assert.Empty(plan!.Objects);
        var missing = PlcBlockExportPlan.Build(inventory, Op(PlcSourceFolderMethods.ExportBlocks, o => { o.DirectoryPath = "/tmp"; o.BlockPath = "PLC_1/Units/Nope/Blocks"; }));
        Assert.Equal(("target_not_found", true), (missing.Category, missing.Error!.Contains("Software unit 'Nope' was not found")));
    }

    [Fact]
    public void TheManifestListsEveryFileDeterministically()
    {
        var result = new PlcBlockExportResultInfo
        {
            Format = "xml", PlcName = "PLC_1", DeviceName = "S1", Scope = null, Complete = false,
            Files = Enumerable.Range(0, 3).Select(i => new PlcExportedFileInfo { ObjectPath = $"PLC_1/B{i}", Kind = "block", RelativePath = $"Blocks/B{i}.xml", Bytes = i, Sha256 = new string('a', 64) }).ToList(),
            Skipped = new() { new PlcExportSkipInfo { ObjectPath = "PLC_1/C", Kind = "block", Reason = "r", FailureCategory = null } },
        };
        var manifest = PlcFileExports.Manifest(result);
        Assert.Equal(manifest, PlcFileExports.Manifest(result));
        Assert.DoesNotContain("\r", manifest);
        using var document = JsonDocument.Parse(manifest);
        Assert.Equal(new[] { "Blocks/B0.xml", "Blocks/B1.xml", "Blocks/B2.xml" },
            document.RootElement.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("relativePath").GetString()));
        Assert.Equal("PLC_1/C", document.RootElement.GetProperty("skipped")[0].GetProperty("objectPath").GetString());
    }

    [Fact]
    public void GuardsAreInfoOnly()
    {
        foreach (var guard in PlcSourceFolderGuards.Definitions)
        {
            Assert.Equal("info", guard.Severity);
            Assert.Contains(guard, PlcGuardDefinitions.Definitions);
        }
    }
}
