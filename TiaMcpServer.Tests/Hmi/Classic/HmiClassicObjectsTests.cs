using System.Xml.Linq;
using Siemens.Engineering;
using Siemens.Engineering.Hmi;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Hmi;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;
using static TiaMcpServer.Tests.Hmi.HmiTestProjects;

namespace TiaMcpServer.Tests.Hmi.ClassicWinCC;

/// <summary>The worker's Classic WinCC objects (issue #28) against the offline classic doubles (members bound at runtime).</summary>
[Collection(HmiSnapshotStoreCollection.Name)]
public sealed class HmiClassicObjectsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "tia-mcp-classic-tests-" + Guid.NewGuid().ToString("N"));

    public HmiClassicObjectsTests()
    {
        Directory.CreateDirectory(_directory);
        HmiClassicObjects.AcceptHostRootedPathsForTests = true;
    }

    public void Dispose()
    {
        HmiClassicObjects.AcceptHostRootedPathsForTests = false;
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    private static HmiClassicKind Kind(string name) => HmiClassicKinds.Find(name)!;

    private static HmiTarget Panel()
    {
        var target = Classic("Panel_RT");
        target.ScreenFolder.Screens.Add("Main").Number = 1;
        var service = target.ScreenFolder.Folders.Create("Service");
        service.Screens.Add("Settings").Number = 3;
        var motors = target.TagFolder.TagTables.Add("Motors");
        motors.Tags.Add("Speed");
        target.TagFolder.TagTables.Add("Default tag table").IsSystemObject = true;
        target.TextLists.Add("States");
        target.GraphicLists.Add("Lamps");
        return target;
    }

    private string File(string name, string content)
    {
        var path = Path.Combine(_directory, name);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void List_WalksUserFoldersAndReadsSystemObject()
    {
        var target = Panel();
        var project = ProjectWith(DeviceWith("Panel", target));

        var screens = HmiClassicObjects.List(project, new HmiQueryInfo { ClassicKind = HmiClassicKinds.Screens });
        var tags = HmiClassicObjects.List(project, new HmiQueryInfo { ClassicKind = HmiClassicKinds.Tags, TableName = "motors" });
        var tables = HmiClassicObjects.List(project, new HmiQueryInfo { HmiName = "Panel", ClassicKind = HmiClassicKinds.TagTables });

        Assert.Equal(new[] { ("Main", (string?)null), ("Settings", "Service") }, screens.Objects.Select(o => (o.Name, o.FolderPath)));
        Assert.Equal(new[] { "Service" }, screens.Folders);
        Assert.Equal("Panel_RT", screens.HmiName);
        Assert.Equal(("Speed", "Motors"), (Assert.Single(tags.Objects).Name, tags.Objects[0].TableName));
        Assert.True(tables.Objects.Single(o => o.Name == "Default tag table").IsSystemObject);
        Assert.True(screens.IsComplete);
    }

    [Fact]
    public void List_UnifiedSelectionIsTargetKindUnsupportedAndUnifiedOperationsPointToClassic()
    {
        var project = ProjectWith(DeviceWith("Panel", Panel()), DeviceWith("Pc", Unified("Pc_RT")));

        var unified = Assert.Throws<WorkerOperationException>(() => HmiClassicObjects.List(project, new HmiQueryInfo { HmiName = "Pc_RT", ClassicKind = HmiClassicKinds.Screens }));
        var classic = Assert.Throws<WorkerOperationException>(() => HmiSoftwareLocator.FindUnique(project, "Panel"));

        Assert.Equal(WorkerFailureCategories.TargetKindUnsupported, unified.FailureCategory);
        Assert.Equal(WorkerFailureCategories.TargetKindUnsupported, classic.FailureCategory);
        Assert.Contains("list_classic_objects", classic.Message);
    }

    [Fact]
    public void Export_WritesOneFilePerObjectAndRefusesExistingFilesWithoutOverwrite()
    {
        var target = Panel();
        var write = new HmiWriteRequestInfo { ClassicKind = HmiClassicKinds.Screens, DirectoryPath = _directory };

        var outcome = HmiClassicObjects.Export(target, "Panel_RT", Kind(HmiClassicKinds.Screens), write);
        var again = Assert.Throws<WorkerOperationException>(() => HmiClassicObjects.Export(target, "Panel_RT", Kind(HmiClassicKinds.Screens), write));

        Assert.Equal(2, outcome.AppliedProperties.Count);
        Assert.StartsWith($"screens Main -> {Path.Combine(_directory, "Main.xml")}", outcome.AppliedProperties[0]);
        Assert.Contains("<Number>3</Number>", System.IO.File.ReadAllText(Path.Combine(_directory, "Settings.xml")));
        Assert.Equal(WorkerFailureCategories.ValidationError, again.FailureCategory);
        write.Overwrite = true;
        write.ObjectNames = new() { "settings" };
        Assert.Single(HmiClassicObjects.Export(target, "Panel_RT", Kind(HmiClassicKinds.Screens), write).AppliedProperties);
    }

    [Fact]
    public void Export_GraphicListsAreNeverExported()
    {
        var target = Panel();

        var error = Assert.Throws<WorkerOperationException>(() => HmiClassicObjects.Export(target, "Panel_RT", Kind(HmiClassicKinds.GraphicLists),
            new HmiWriteRequestInfo { ClassicKind = HmiClassicKinds.GraphicLists, DirectoryPath = _directory }));

        Assert.Equal(WorkerFailureCategories.TargetKindUnsupported, error.FailureCategory);
        Assert.StartsWith("skipped: reported to crash TIA Portal V21", error.Message);
        Assert.Equal(0, target.GraphicLists.Items[0].Exports);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public void Import_IntoTagTableAndFolderReportsTheNewObjects()
    {
        var target = Panel();
        var tagFile = File("tags.xml", "<Document><Object Name=\"Pressure\"/></Document>");
        var screenFile = File("screen.xml", "<Document><Object Name=\"Detail\"/><Hmi.Screen.Screen><AttributeList><Name>Detail</Name><Number>2</Number></AttributeList></Hmi.Screen.Screen></Document>");

        var tags = HmiClassicObjects.Import(target, "Panel_RT", Kind(HmiClassicKinds.Tags), new HmiWriteRequestInfo { ClassicKind = HmiClassicKinds.Tags, TableName = "Motors", FilePath = tagFile });
        var screens = HmiClassicObjects.Import(target, "Panel_RT", Kind(HmiClassicKinds.Screens), new HmiWriteRequestInfo { ClassicKind = HmiClassicKinds.Screens, GroupPath = "Service", FilePath = screenFile });

        Assert.Contains("tags Pressure", tags.AppliedProperties);
        Assert.Contains(target.TagFolder.TagTables.Items[0].Tags.Items, t => t.Name == "Pressure");
        Assert.Contains("screens Detail", screens.AppliedProperties);
        Assert.Contains(screens.AppliedProperties, p => p.StartsWith("screenNumbers checked (1 in the file, 2 existing)", StringComparison.Ordinal));
        Assert.Contains(target.ScreenFolder.Folders.Items[0].Screens.Items, s => s.Name == "Detail");
    }

    [Theory]
    [InlineData("<Document><Hmi.Screen.Screen><AttributeList><Name>Other</Name><Number>1</Number></AttributeList></Hmi.Screen.Screen></Document>", false)]
    [InlineData("<Document><Hmi.Screen.Screen><AttributeList><Name>A</Name><Number>7</Number></AttributeList></Hmi.Screen.Screen><Hmi.Screen.Screen><AttributeList><Name>B</Name><Number>7</Number></AttributeList></Hmi.Screen.Screen></Document>", false)]
    [InlineData("<Document><Hmi.Screen.Screen><AttributeList><Name>Settings</Name><Number>1</Number></AttributeList></Hmi.Screen.Screen></Document>", true)]
    public void Import_ScreenNumberCollisionIsRefusedBeforeImport(string content, bool numberFromExport)
    {
        var target = Panel();
        if (numberFromExport) target.ScreenFolder.Screens.Items[0].RefuseNumberAttribute = true;
        var file = File("clash.xml", content.Replace("</Document>", "<Object Name=\"X\"/></Document>", StringComparison.Ordinal));

        var error = Assert.Throws<WorkerOperationException>(() => HmiClassicObjects.Import(target, "Panel_RT", Kind(HmiClassicKinds.Screens),
            new HmiWriteRequestInfo { ClassicKind = HmiClassicKinds.Screens, FilePath = file, Overwrite = true }));

        Assert.Equal(WorkerFailureCategories.ValidationError, error.FailureCategory);
        Assert.Contains("V21 pitfall 4", error.Message);
        Assert.DoesNotContain(target.ScreenFolder.Screens.Items, s => s.Name == "X");
        Assert.Equal(numberFromExport ? 1 : 0, target.ScreenFolder.Screens.Items[0].Exports);
    }

    [Fact]
    public void Import_WithoutOverwriteOfAnExistingNameIsTiaPortalsRejection()
    {
        var target = Panel();
        var file = File("lists.xml", "<Document><Object Name=\"States\"/></Document>");

        Assert.Throws<WorkerOperationException>(() => HmiClassicObjects.Import(target, "Panel_RT", Kind(HmiClassicKinds.TextLists),
            new HmiWriteRequestInfo { ClassicKind = HmiClassicKinds.TextLists, FilePath = file }));
        var replaced = HmiClassicObjects.Import(target, "Panel_RT", Kind(HmiClassicKinds.TextLists),
            new HmiWriteRequestInfo { ClassicKind = HmiClassicKinds.TextLists, FilePath = file, Overwrite = true });

        Assert.Contains(replaced.AppliedProperties, p => p.StartsWith("(no new textLists object", StringComparison.Ordinal));
    }

    [Fact]
    public void ImportScreenGlobalElements_UsesTheTargetMember()
    {
        var target = Panel();
        var file = File("global.xml", "<Document/>");

        var outcome = HmiClassicObjects.Import(target, "Panel_RT", Kind(HmiClassicKinds.ScreenGlobalElements),
            new HmiWriteRequestInfo { ClassicKind = HmiClassicKinds.ScreenGlobalElements, FilePath = file, Overwrite = true });

        Assert.Equal("<Document/>|Override", Assert.Single(target.GlobalElementImports));
        Assert.Contains(outcome.AppliedProperties, p => p.Contains("not read back", StringComparison.Ordinal));
    }

    [Fact]
    public void DeleteObjectAndFolders_AreReadBack()
    {
        var target = Panel();

        HmiClassicObjects.Delete(target, Kind(HmiClassicKinds.Tags), new HmiWriteRequestInfo { ClassicKind = HmiClassicKinds.Tags, ObjectName = "speed" });
        var created = HmiClassicObjects.CreateFolder(target, Kind(HmiClassicKinds.TagTables), new HmiWriteRequestInfo { ClassicKind = HmiClassicKinds.TagTables, GroupPath = "Line1" });
        var nested = HmiClassicObjects.CreateFolder(target, Kind(HmiClassicKinds.Screens), new HmiWriteRequestInfo { ClassicKind = HmiClassicKinds.Screens, GroupPath = "Service/Diagnostics" });
        var removed = HmiClassicObjects.DeleteFolder(target, Kind(HmiClassicKinds.Screens), new HmiWriteRequestInfo { ClassicKind = HmiClassicKinds.Screens, GroupPath = "Service" });
        var missing = Assert.Throws<WorkerOperationException>(() => HmiClassicObjects.Delete(target, Kind(HmiClassicKinds.Screens), new HmiWriteRequestInfo { ClassicKind = HmiClassicKinds.Screens, ObjectName = "Nope" }));

        Assert.Empty(target.TagFolder.TagTables.Items[0].Tags.Items);
        Assert.Equal(new[] { "Create", "readBack present" }, created.AppliedProperties);
        Assert.Equal("Line1", Assert.Single(target.TagFolder.Folders.Items).Name);
        Assert.Equal("Service/Diagnostics", nested.ObjectName);
        Assert.Equal(new[] { "Delete", "readBack gone" }, removed.AppliedProperties);
        Assert.Empty(target.ScreenFolder.Folders.Items);
        Assert.Equal(WorkerFailureCategories.TargetNotFound, missing.FailureCategory);
    }

    [Fact]
    public void MissingMembersAreApiMemberMissing()
    {
        var target = Panel();

        // The double models no ScreenPopupFolder: a property absent from the installation fails before anything changes.
        var error = Assert.Throws<WorkerOperationException>(() => HmiClassicObjects.CreateFolder(target, Kind(HmiClassicKinds.ScreenPopups),
            new HmiWriteRequestInfo { ClassicKind = HmiClassicKinds.ScreenPopups, GroupPath = "P" }));

        Assert.Equal(WorkerFailureCategories.ApiMemberMissing, error.FailureCategory);
        Assert.Contains("ScreenPopupFolder", error.Message);
    }

    [Fact]
    public void ScreenNumbers_ReadAndConflicts()
    {
        var document = XDocument.Parse("<Document><Hmi.Screen.Screen><AttributeList><Name>A</Name><Number>4</Number></AttributeList></Hmi.Screen.Screen><Hmi.Screen.ScreenTemplate><AttributeList><Name>T</Name><Number>4</Number></AttributeList></Hmi.Screen.ScreenTemplate></Document>");

        var read = HmiClassicScreenNumbers.Read(document);

        Assert.Equal(new[] { ("A", 4) }, read);
        Assert.Empty(HmiClassicScreenNumbers.Conflicts(read, new[] { ("a", 4) }));
        Assert.Single(HmiClassicScreenNumbers.Conflicts(read, new[] { ("B", 4) }));
    }
}
