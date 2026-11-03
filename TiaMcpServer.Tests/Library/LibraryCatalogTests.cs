using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Library;
using Xunit;

namespace TiaMcpServer.Tests.Library;

/// <summary>The library operation catalog, its policy classification, capability mapping and argument validation.</summary>
public sealed class LibraryCatalogTests
{
    private const string Plant = @"C:\Libraries\Plant\Plant.al21";

    [Fact]
    public void CatalogOperations_EqualTheContractLists()
    {
        Assert.Equal(LibraryMethods.ReadOperations.Order(), LibraryCatalog.ReadOperationNames.Order());
        Assert.Equal(LibraryMethods.WriteOperations.Order(), LibraryCatalog.WriteOperationNames.Order());
        Assert.Empty(LibraryMethods.ReadOperations.Intersect(LibraryMethods.WriteOperations));
        Assert.Equal(39, LibraryMethods.WriteOperations.Count);
        Assert.Equal(14, LibraryMethods.ReadOperations.Count);
        Assert.Equal(10, LibraryMethods.LifecycleOperations.Count);
    }

    [Fact]
    public void Policy_ReadsAndPlanningObserve_WritesMutate_UnknownIsDenied()
    {
        foreach (var method in LibraryMethods.Read.Append(LibraryMethods.Plan))
        {
            Assert.Equal(OperationCapability.Observe, OperationPolicyCatalog.GetCapability(method));
            Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, method));
            Assert.True(OperationPolicyCatalog.RequiresExpectedSessionIdentity(method));
        }

        foreach (var method in LibraryMethods.Write)
        {
            Assert.Equal(OperationCapability.ProjectMutation, OperationPolicyCatalog.GetCapability(method));
            Assert.False(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, method));
            Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadWrite, method));
            Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.Full, method));
        }

        Assert.Null(OperationPolicyCatalog.GetCapability("library_export_type"));
    }

    [Fact]
    public void Capabilities_MapEveryMethodToItsTool_StandaloneOnly()
    {
        foreach (var method in LibraryMethods.Read)
            Assert.Equal(LibraryMethods.ReadTool, ProjectCapabilityCatalog.OperationFor(method));
        foreach (var method in LibraryMethods.Write.Append(LibraryMethods.Plan))
            Assert.Equal(LibraryMethods.WriteTool, ProjectCapabilityCatalog.OperationFor(method));
        foreach (var tool in new[] { LibraryMethods.ReadTool, LibraryMethods.WriteTool })
        {
            Assert.True(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.StandaloneProject, tool));
            Assert.False(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.LocalSession, tool));
            Assert.False(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.ServerProject, tool));
            Assert.True(ProjectCapabilityCatalog.RequiresBoundProject(tool));
        }
    }

    [Fact]
    public void MethodNames_RoundTrip()
    {
        Assert.Equal("library_get_type", LibraryMethods.ForOperation(LibraryMethods.GetLibraryType));
        Assert.Equal(LibraryMethods.CloseGlobalLibrary, LibraryMethods.OperationOf("library_close_global_library"));
        Assert.Null(LibraryMethods.OperationOf("library_plan"));
        Assert.Null(LibraryMethods.OperationOf("get_type"));
        Assert.True(LibraryMethods.IsWorkerMethod(LibraryMethods.Plan));
        Assert.All(LibraryMethods.LifecycleOperations, operation => Assert.True(LibraryMethods.IsLifecycle(operation)));
        Assert.False(LibraryMethods.IsLifecycle(LibraryMethods.RenameType));
    }

    public static TheoryData<string, object> Valid => new()
    {
        { "read", new LibraryOperationRequest { OperationId = "a", Operation = "list_libraries" } },
        { "read", new LibraryOperationRequest { OperationId = "a", Operation = "get_library_tree", Library = Plant, FolderPath = "/Motors", Limit = 500, MaxDepth = 32 } },
        { "read", new LibraryOperationRequest { OperationId = "a", Operation = "find_type_instances", Library = "project", VersionGuid = Guid.NewGuid().ToString() } },
        { "read", new LibraryOperationRequest { OperationId = "a", Operation = "compare_libraries", Library = "project", TargetLibrary = Plant, MaxDepth = 64 } },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "open_global_library", Path = Plant, OpenMode = "ReadWrite" } },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "open_global_library", Path = @"\\server\share\Plant.al21", Upgrade = true } },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "retrieve_global_library", Path = @"C:\A\Plant.zal21", TargetDirectory = @"C:\Out" } },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "release_type_version", Library = "project", TypePath = "/Motors/Pump", Version = "1.0.1", VersionNumber = "2.0.0", Author = "", Comment = "ok" } },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "update_project", Library = Plant, FolderPath = "/" } },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "instantiate_master_copy", Library = "project", MasterCopyName = "Valve",
            Target = new LibraryTargetInfo { Kind = "tagTableGroup", PlcName = "PLC_1", FolderPath = "/IO" }, MasterCopyMode = "Replace", NewName = "Valve_1" } },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "instantiate_master_copy", Library = "project", MasterCopyName = "Station",
            Target = new LibraryTargetInfo { Kind = "deviceItem", Parent = new() { DeviceName = "PLC_1", ItemPath = new() { new() { Name = "Rack_0" } } } } } },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "create_master_copy", Library = "project", Source = new LibrarySourceInfo { Kind = "tagTable", PlcName = "PLC_1", Path = "/IO/Inputs" } } },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "instantiate_master_copy", Library = "project", MasterCopyName = "Conveyor_Unit",
            Target = new LibraryTargetInfo { Kind = "softwareUnits", PlcName = "PLC_1" }, MasterCopyMode = "ThrowIfExists" } },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "instantiate_master_copy", Library = "project", MasterCopyName = "Sources",
            Target = new LibraryTargetInfo { Kind = "externalSources", PlcName = "PLC_1" }, MasterCopyMode = "Rename" } },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "close_global_library", Library = Plant, PortalConfirmations = "accept" } },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "rename_type", Library = "project", TypePath = "A", NewName = "B", PortalConfirmations = "decline" } },
        // Only create_global_library's name is a file name; object names keep the plain name rules.
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "rename_type", Library = "project", TypePath = "A", NewName = "Motor:Control" } },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "create_global_library", TargetDirectory = @"\\server\share\Libraries", Name = "Plant Library 2.0" } },
        // Issue #17 additions.
        { "read", new LibraryOperationRequest { OperationId = "a", Operation = "get_supported_export_formats", Library = "project", TypePath = "/Motors/Pump" } },
        { "read", new LibraryOperationRequest { OperationId = "a", Operation = "compare_types", Library = "project", TypePath = "A", TargetLibrary = Plant, TargetTypePath = "A" } },
        { "read", new LibraryOperationRequest { OperationId = "a", Operation = "compare_type_versions", Library = "project", TypePath = "A", Version = "1.0.0", TargetTypePath = "A", TargetVersion = "1.0.1" } },
        { "read", new LibraryOperationRequest { OperationId = "a", Operation = "compare_master_copies", Library = "project", MasterCopyName = "A", MasterCopyFolderPath = "/F", TargetMasterCopyName = "B" } },
        { "read", new LibraryOperationRequest { OperationId = "a", Operation = "get_library_tree", Library = "project", MasterCopyFolderPath = "/Valves" } },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "retrieve_global_library", Path = @"C:\A\Plant.zal21", TargetDirectory = @"C:\Out", Upgrade = true } },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "archive_global_library", Library = Plant, TargetDirectory = @"C:\Out", Name = "Plant_2026", ArchivationMode = "DiscardRestorableDataAndCompressed" } },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "save_global_library_as", Library = Plant, TargetDirectory = @"C:\Out" } },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "clean_up_library", Library = "project", FolderPath = "/", CleanUpMode = "DeleteUnusedTypes" } },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "clean_up_library", Library = Plant, TypePath = "/Motors/Pump" } },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "harmonize_project", Library = "project", FolderPath = "/", HarmonizeOptions = new[] { "HarmonizePaths", "HarmonizeNames" }, PlcName = "PLC_1" } },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "update_library", Library = "project", FolderPath = "/", TargetLibrary = Plant,
            ForceUpdateMode = "SetOnlyHigherUpdatedVersionAsDefault", DeleteUnusedVersionsMode = "DoNotDelete", StructureConflictResolutionMode = "RetainStructure" } },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "update_project", Library = "project", FolderPath = "/", DeleteUnusedVersionsMode = "AutomaticallyDelete" } },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "delete_type", Library = "project", TypeGuid = Guid.NewGuid().ToString() } },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "set_type_do_not_use", Library = "project", TypePath = "A", DoNotUse = true } },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "create_type_folder", Library = "project", FolderPath = "/Motors", NewName = "Drives" } },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "create_master_copy_folder", Library = Plant, NewName = "Valves" } },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "rename_master_copy_folder", Library = Plant, MasterCopyFolderPath = "/Valves", NewName = "Valves2" } },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "create_type_from_documents", Library = "project", DocumentDirectory = @"C:\Docs", DocumentName = "NewType", PlcName = "PLC_1", LibraryImportOptions = "SkipInactiveCultures" } },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "create_version_from_documents", Library = "project", TypePath = "A", DocumentDirectory = @"C:\Docs", DocumentName = "A", CreateOption = "Override" } },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "export_type_version", Library = "project", TypePath = "A", FilePath = @"C:\Out\A.xml", ExportOptions = "WithDefaults", DocumentInfoOptions = "All", Overwrite = true } },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "export_type_version_as_documents", Library = "project", TypePath = "A", DocumentDirectory = @"C:\Out", DocumentName = "A", ExportFormat = "SimaticSD", LibraryExportOptions = "WithLibraryVersionInfoFile" } },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "copy_master_copy", Library = "project", MasterCopyName = "Valve", MasterCopyFolderPath = "/A", TargetLibrary = Plant, TargetMasterCopyFolderPath = "/B" } },
    };

    [Theory]
    [MemberData(nameof(Valid))]
    public void Validation_AcceptsDocumentedShapes(string tool, object item)
    {
        var items = new[] { (LibraryOperationRequest)item };
        var result = tool == "read" ? LibraryCatalog.ValidateRead(items) : LibraryCatalog.ValidateWrite(items);
        Assert.True(result.IsValid, result.Error);
    }

    public static TheoryData<string, object, string> Invalid => new()
    {
        { "read", new LibraryOperationRequest { OperationId = "a", Operation = "open_global_library", Path = Plant }, "use library_write" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "get_type", Library = "project", TypePath = "X" }, "use library_read" },
        { "read", new LibraryOperationRequest { OperationId = "a", Operation = "export_type_version" }, "use library_write" },
        { "read", new LibraryOperationRequest { OperationId = "a", Operation = "export_type" }, "unknown library_read operation" },
        { "read", new LibraryOperationRequest { OperationId = "a", Operation = "get_type", Library = "project" }, "typePath or typeGuid" },
        { "read", new LibraryOperationRequest { OperationId = "a", Operation = "get_type", Library = "project", TypePath = "A", TypeGuid = Guid.NewGuid().ToString() }, "not both" },
        { "read", new LibraryOperationRequest { OperationId = "a", Operation = "get_type", Library = "Plant", TypePath = "A" }, "'project' or the absolute path" },
        { "read", new LibraryOperationRequest { OperationId = "a", Operation = "get_library_tree", Library = "project", Limit = 501 }, "limit must be 1 to 500" },
        { "read", new LibraryOperationRequest { OperationId = "a", Operation = "get_library_tree", Library = "project", FolderPath = "/A//B" }, "folderPath" },
        { "read", new LibraryOperationRequest { OperationId = "a", Operation = "get_library_tree", Library = "project", MaxDepth = 33 }, "maxDepth must be 1 to 32" },
        { "read", new LibraryOperationRequest { OperationId = "a", Operation = "get_instance_info", BlockPath = "Main", PlcTypePath = "T" }, "exactly one of blockPath or plcTypePath" },
        { "read", new LibraryOperationRequest { OperationId = "a", Operation = "compare_libraries", Library = "project", TargetLibrary = "project" }, "different library" },
        { "read", new LibraryOperationRequest { OperationId = "a", Operation = "update_check", Library = Plant, UpdateCheckMode = "ReportAll" }, "updateCheckMode must be one of ReportOutOfDateOnly, ReportOutOfDateAndUpToDate" },
        // Issue #17 additions.
        { "read", new LibraryOperationRequest { OperationId = "a", Operation = "compare_types", Library = "project", TypePath = "A" }, "targetTypePath or targetTypeGuid" },
        { "read", new LibraryOperationRequest { OperationId = "a", Operation = "compare_type_versions", Library = "project", TypePath = "A", TargetVersion = "1.0.0" }, "targetVersion needs the compared type" },
        { "read", new LibraryOperationRequest { OperationId = "a", Operation = "compare_master_copies", Library = "project", MasterCopyName = "A" }, "'targetMasterCopyName' is required" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "archive_global_library", Library = "project", TargetDirectory = @"C:\Out", Name = "A" }, "user global library" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "archive_global_library", Library = Plant, TargetDirectory = @"C:\Out", Name = "A", ArchivationMode = "Zip" }, "archivationMode must be one of" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "clean_up_library", Library = Plant, FolderPath = "/", CleanUpMode = "DeleteUnusedTypes" }, "project library only" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "update_project", Library = "project", FolderPath = "/", ForceUpdateMode = "NoDefaultVersionChange" }, "takes only deleteUnusedVersionsMode" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "update_project", Library = Plant, FolderPath = "/", ForceUpdateMode = "NoDefaultVersionChange" }, "together" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "update_library", Library = "project", FolderPath = "/", TargetLibrary = Plant, DeleteUnusedVersionsMode = "DoNotDelete" }, "together" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "harmonize_project", Library = "project", FolderPath = "/", HarmonizeOptions = new[] { "None" } }, "harmonizeOptions must be one of" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "harmonize_project", Library = "project", FolderPath = "/", HarmonizeOptions = new[] { "HarmonizeNames", "HarmonizeNames" } }, "distinct" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "delete_type_folder", Library = "project", FolderPath = "/" }, "root type folder" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "delete_master_copy_folder", Library = "project", MasterCopyFolderPath = "/" }, "root master copy folder" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "set_type_do_not_use", Library = "project", TypePath = "A" }, "'doNotUse' is required" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "create_type_from_documents", Library = "project", DocumentDirectory = @"C:\Docs", DocumentName = @"..\X" }, "documentName must be a valid Windows file name" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "export_type_version", Library = "project", TypePath = "A", FilePath = "out.xml" }, "filePath must be an absolute path" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "export_type_version", Library = "project", TypePath = "A", FilePath = @"C:\A.xml", ExportOptions = "All" }, "exportOptions must be one of" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "export_type_version_as_documents", Library = "project", TypePath = "A", DocumentDirectory = @"C:\A", DocumentName = "A" }, "'exportFormat' is required" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "open_global_library", Path = @"C:\A\Plant.zal21" }, "use retrieve_global_library" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "open_global_library", Path = Plant, Upgrade = true, OpenMode = "ReadOnly" }, "not valid with upgrade" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "open_global_library", Path = @"Plant.al21" }, "absolute path" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "open_global_library", Path = @"\\./COM1" }, "device or extended-length path" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "retrieve_global_library", Path = @"\\?/UNC/server/share/Plant.zal21", TargetDirectory = @"C:\Out" }, "path must not be a Win32 device" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "create_global_library", TargetDirectory = @"C:\New", Name = @"..\..\Temp\Evil" }, "name must be a valid Windows file name" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "create_global_library", TargetDirectory = @"\\.\C:\New", Name = "Lib" }, "targetDirectory must not be a Win32 device" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "open_global_library", Path = @"C:\A\..\Plant.al21" }, "'..'" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "open_global_library", Path = Plant, OpenMode = "Exclusive" }, "openMode must be one of ReadOnly, ReadWrite" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "save_global_library", Library = "project" }, "names a global library" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "rename_type_folder", Library = "project", FolderPath = "/", NewName = "X" }, "root type folder" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "rename_type", Library = "project", TypePath = "A", NewName = "B/C" }, "must not contain '/'" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "release_type_version", Library = "project", TypePath = "A", VersionNumber = "2" }, "versionNumber" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "release_type_version", Library = "project", TypePath = "A", VersionNumber = "2.0", DependenciesMode = "CreateDependencies" }, "dependenciesMode" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "delete_type_version", Library = "project", Version = "1.0.0" }, "version needs the type" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "update_project", Library = "project", TypePath = "A", FolderPath = "/" }, "exactly one type" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "instantiate_master_copy", Library = "project", MasterCopyName = "S",
            Target = new LibraryTargetInfo { Kind = "devices" }, MasterCopyMode = "Replace" }, "takes no MasterCopyMode" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "instantiate_master_copy", Library = "project", MasterCopyName = "S",
            Target = new LibraryTargetInfo { Kind = "deviceItem" } }, "target.parent is required" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "instantiate_type_version", Library = "project", TypePath = "A",
            Target = new LibraryTargetInfo { Kind = "tagTableGroup" } }, "target.kind must be one of blockGroup, typeGroup" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "create_master_copy", Library = "project", Source = new LibrarySourceInfo { Kind = "block", PlcName = "PLC_1", Path = "Main" } }, "source.plcName" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "create_master_copy", Library = "project", Source = new LibrarySourceInfo { Kind = "alarmTextList", Path = "X" } }, "source.kind" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "delete_master_copy", Library = "project" }, "'masterCopyName' is required" },
        { "write", new LibraryOperationRequest { OperationId = "a", Operation = "close_global_library", Library = Plant, PortalConfirmations = "Yes" }, "portalConfirmations must be one of decline, accept" },
        { "read", new LibraryOperationRequest { OperationId = "a", Operation = "list_libraries", PortalConfirmations = "decline" }, "'portalConfirmations' is not valid" },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Validation_RejectsInvalidInput(string tool, object item, string fragment)
    {
        var items = new[] { (LibraryOperationRequest)item };
        var result = tool == "read" ? LibraryCatalog.ValidateRead(items) : LibraryCatalog.ValidateWrite(items);
        Assert.False(result.IsValid);
        Assert.Equal("validation_error", result.Category);
        Assert.Contains(fragment, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void AccessMode_ReadOnlyRefusesWrites()
    {
        var items = new[] { new LibraryOperationRequest { OperationId = "a", Operation = "rename_type" } };
        Assert.Single(LibraryCatalog.ValidateAccessMode(items, McpAccessMode.ReadOnly));
        Assert.Empty(LibraryCatalog.ValidateAccessMode(items, McpAccessMode.ReadWrite));
    }

    [Fact]
    public void ToRequest_AppliesTheDocumentedDefaults()
    {
        var tree = LibraryCatalog.ToRequest(new LibraryOperationRequest { OperationId = "a", Operation = "get_library_tree", Library = "project" });
        Assert.Equal((LibraryPolicy.DefaultTreeDepth, LibraryPolicy.DefaultPageLimit, 0, true), (tree.MaxDepth, tree.Limit, tree.Offset, tree.IncludeVersions));
        var compare = LibraryCatalog.ToRequest(new LibraryOperationRequest { OperationId = "a", Operation = "compare_libraries", Library = "project", TargetLibrary = Plant });
        Assert.Equal((LibraryPolicy.DefaultCompareDepth, LibraryPolicy.DefaultCompareElements), (compare.MaxDepth, compare.MaxElements));
        var open = LibraryCatalog.ToRequest(new LibraryOperationRequest { OperationId = "a", Operation = "open_global_library", Path = Plant });
        Assert.False(open.Upgrade);
        Assert.Null(open.OpenMode);
        // Every write item carries an explicit policy to the worker (omitted: decline); reads carry none.
        Assert.Equal("decline", open.PortalConfirmations);
        Assert.Equal("accept", LibraryCatalog.ToRequest(new LibraryOperationRequest { OperationId = "a", Operation = "close_global_library", Library = Plant, PortalConfirmations = "accept" }).PortalConfirmations);
        Assert.Null(tree.PortalConfirmations);
    }

    [Fact]
    public void LibraryKeys_NormalizeGlobalPaths_AndKeepTheProjectSelector()
    {
        Assert.Equal(LibraryCatalog.Key(Plant), LibraryCatalog.Key(@"c:/libraries/plant/plant.al21"));
        Assert.Equal("project", LibraryCatalog.Key("project"));
        Assert.Equal(LibraryCatalog.Key(Plant), LibraryCatalog.LibraryKey(new LibraryOperationRequest { Operation = "open_global_library", Path = Plant }));
        Assert.Null(LibraryCatalog.LibraryKey(new LibraryOperationRequest { Operation = "create_global_library", TargetDirectory = @"C:\New" }));
    }
}
