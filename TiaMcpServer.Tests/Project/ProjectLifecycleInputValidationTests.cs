using Siemens.Engineering;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Project;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public sealed class ProjectLifecycleInputValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("relative.ap21")]
    public void InvalidFilePath_IsValidationError(string? path)
        => ValidationError(() => ProjectLifecycleService.RequireAbsoluteFile(path!, "ProjectPath"));

    [Theory]
    [InlineData(@"\\?\C:\Projects\Plant.ap21")]
    [InlineData(@"\\.\C:\Projects")]
    [InlineData(@"\??\C:\Projects")]
    [InlineData(@"C:\Projects\NUL")]
    [InlineData(@"C:\Projects\COM1.ap21")]
    [InlineData(@"C:\Projects\Plant. ")]
    public void DevicePathsAndReservedNames_AreValidationErrors_InTheWorker(string path)
        => ValidationError(() => ProjectLifecycleService.RequireCallerPathShape(path, "ProjectPath"));

    [Fact]
    public void AnOrdinaryAbsolutePath_PassesTheShapeCheck()
        => ProjectLifecycleService.RequireCallerPathShape(@"C:\Projects\Plant\Plant.ap21", "ProjectPath");

    public static TheoryData<TiaMcpServer.ProjectLifecycle.LifecycleWriteItem, string> HostRefusals => new()
    {
        { new("open_project") { ProjectPath = @"\\?\C:\Projects\Plant.ap21" }, "projectPath" },
        { new("create_project") { ProjectDirectory = @"\\.\C:\Projects", ProjectName = "Plant" }, "projectDirectory" },
        { new("save_project_as") { TargetDirectory = @"C:\Projects\CON", TargetName = "Copy" }, "targetDirectory" },
        { new("archive_project") { ArchiveDirectory = @"\??\C:\Archives", ArchiveName = "Plant" }, "archiveDirectory" },
        { new("retrieve_project") { ArchivePath = @"C:\Archives\LPT1.zap21", TargetDirectory = @"C:\Projects" }, "archivePath" },
        { new("retrieve_project") { ArchivePath = @"C:\Archives\Plant.zap21", TargetDirectory = @"\\?\C:\Projects" }, "targetDirectory" },
    };

    [Theory]
    [MemberData(nameof(HostRefusals))]
    public void Host_RefusesDevicePathsAndReservedNames_ForEveryLifecyclePath(TiaMcpServer.ProjectLifecycle.LifecycleWriteItem item, string member)
        => Assert.Contains($"'{member}'", TiaMcpServer.ProjectLifecycle.LifecycleWriteDomain.PathRefusal(item), StringComparison.Ordinal);

    [Fact]
    public void Host_KeepsOrdinaryPaths_AndLeavesMissingOnesToTheOperation()
    {
        Assert.Null(TiaMcpServer.ProjectLifecycle.LifecycleWriteDomain.PathRefusal(new("open_project") { ProjectPath = @"C:\Projects\Plant\Plant.ap21" }));
        Assert.Null(TiaMcpServer.ProjectLifecycle.LifecycleWriteDomain.PathRefusal(new("retrieve_project") { ArchivePath = @"\\server\share\Plant.zap21", TargetDirectory = @"C:\Projects" }));
        Assert.Null(TiaMcpServer.ProjectLifecycle.LifecycleWriteDomain.PathRefusal(new("create_project")));
    }

    [Theory]
    [InlineData(@"\\?\C:\Projects\Old.ap21")]
    [InlineData(@"C:\Projects\AUX.ap21")]
    public void SecondaryProjects_RefuseDevicePathsAndReservedNames(string path)
    {
        var item = new TiaMcpServer.Library.LibraryOperationRequest { OperationId = "o", Operation = TiaMcpServer.Contracts.Library.LibraryMethods.OpenSecondaryProject, Path = path };
        Assert.False(TiaMcpServer.Library.LibraryCatalog.ValidateWrite(new[] { item }).IsValid);
        var retrieve = new TiaMcpServer.Library.LibraryOperationRequest { OperationId = "r", Operation = TiaMcpServer.Contracts.Library.LibraryMethods.RetrieveSecondaryProject, Path = @"C:\Archives\Old.zap21", TargetDirectory = path };
        Assert.False(TiaMcpServer.Library.LibraryCatalog.ValidateWrite(new[] { retrieve }).IsValid);
    }

    [Fact]
    public void MissingFile_IsValidationError()
        => ValidationError(() => ProjectLifecycleService.RequireAbsoluteFile(MissingPath(), "ProjectPath"));

    [Theory]
    [InlineData(null, false)]
    [InlineData("", true)]
    [InlineData(" ", false)]
    [InlineData("relative", true)]
    public void InvalidDirectoryPath_IsValidationError(string? path, bool mustExist)
        => ValidationError(() => ProjectLifecycleService.RequireAbsoluteDirectory(path!, "TargetDirectory", mustExist));

    [Fact]
    public void MissingRequiredDirectory_IsValidationError()
        => ValidationError(() => ProjectLifecycleService.RequireAbsoluteDirectory(MissingPath(), "TargetDirectory", mustExist: true));

    [Fact]
    public void MissingArchiveDirectory_IsValidationError()
        => ValidationError(() => ProjectLifecycleService.RequireDirectoryExists(MissingPath(), "ArchiveDirectory"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void MissingName_IsValidationError(string? name)
        => ValidationError(() => ProjectLifecycleService.RequireName(name, "TargetName"));

    [Theory]
    [InlineData("")]
    [InlineData("unsupported")]
    public void InvalidArchiveMode_IsValidationError(string mode)
        => ValidationError(() => ProjectLifecycleService.ParseArchiveMode(mode));

    [Fact]
    public void ValidPathsAndName_AreAccepted()
    {
        ProjectLifecycleService.RequireAbsoluteFile(typeof(ProjectLifecycleInputValidationTests).Assembly.Location, "ProjectPath");
        ProjectLifecycleService.RequireAbsoluteDirectory(Path.GetTempPath(), "TargetDirectory", mustExist: true);
        ProjectLifecycleService.RequireAbsoluteDirectory(MissingPath(), "ArchiveDirectory", mustExist: false);
        ProjectLifecycleService.RequireDirectoryExists(Path.GetTempPath(), "ArchiveDirectory");
        ProjectLifecycleService.RequireName("Fixture", "TargetName");
    }

    [Theory]
    [InlineData("None", ProjectArchivationMode.None)]
    [InlineData("DiscardRestorableData", ProjectArchivationMode.DiscardRestorableData)]
    [InlineData("compressed", ProjectArchivationMode.Compressed)]
    [InlineData("DiscardRestorableDataAndCompressed", ProjectArchivationMode.DiscardRestorableDataAndCompressed)]
    public void ValidArchiveMode_IsParsed(string mode, ProjectArchivationMode expected)
        => Assert.Equal(expected, ProjectLifecycleService.ParseArchiveMode(mode));

    private static string MissingPath()
        => Path.Combine(Path.GetTempPath(), "tia-missing-" + Guid.NewGuid().ToString("N"));

    private static void ValidationError(Action validate)
    {
        // The generic mapper must let the categorized failure reach Program's dedicated catch.
        var error = Assert.Throws<WorkerOperationException>(() => WorkerExecution.Execute(() =>
        {
            validate();
            return new WorkerResponse { Success = true };
        }, () => 0));

        Assert.Equal(WorkerFailureCategories.ValidationError, error.FailureCategory);
    }
}
