using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.TextGraphicList;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Hmi;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Hmi.UnifiedExtensions;

/// <summary>
/// The YAML route of export_text_lists/import_text_lists (HmiTextListComposition.Export/Import(DirectoryInfo, String), as
/// rickgaiser/TiaMcp calls them on V21) against the offline doubles, whose HmiTextList has no SimaticML Export. The caller
/// paths are Windows drive paths (TechnologyObjectPolicy.ResolveLocalFile), so the file cases run on Windows only.
/// </summary>
[Collection(HmiSnapshotStoreCollection.Name)]
public sealed class HmiTextListFilesTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "tia-mcp-textlist-tests-" + Guid.NewGuid().ToString("N"));

    public HmiTextListFilesTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        try { Directory.Delete(directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static HmiSoftware Software()
    {
        var software = new HmiSoftware { Name = "HMI_1" };
        software.HmiTextLists.Items.Add(new HmiTextList { Name = "States" });
        return software;
    }

    [Fact]
    public void Export_UsesTheCompositionYamlRouteWhenTheListHasNoSimaticMlExport()
    {
        if (!OperatingSystem.IsWindows()) return;
        var software = Software();

        var outcome = HmiTextListFiles.ExportLists(software, new HmiWriteRequestInfo { HmiName = "HMI_1", DirectoryPath = directory });
        var again = Assert.Throws<WorkerOperationException>(() => HmiTextListFiles.ExportLists(software, new HmiWriteRequestInfo { HmiName = "HMI_1", DirectoryPath = directory }));
        var named = Assert.Throws<WorkerOperationException>(() => HmiTextListFiles.ExportLists(software, new HmiWriteRequestInfo { HmiName = "HMI_1", DirectoryPath = directory, TextListNames = new() { "States" } }));

        Assert.Equal("route yaml " + HmiTextListFiles.YamlExportMember, outcome.AppliedProperties[0]);
        Assert.Equal(software.HmiTextLists.ListsYaml, File.ReadAllText(Path.Combine(directory, "textlists.hmi.yml")));
        Assert.True(File.Exists(Path.Combine(directory, "textlists.TextLibrary.hmi.yml")));
        Assert.Contains("textList States", outcome.AppliedProperties);
        Assert.Equal(WorkerFailureCategories.ValidationError, again.FailureCategory);
        Assert.Contains("overwrite:true", again.Message);
        Assert.Equal(WorkerFailureCategories.ValidationError, named.FailureCategory);
        Assert.Contains("omit textListNames", named.Message);
    }

    [Fact]
    public void Import_OfAHmiYmlFileCopiesItWithItsTextLibraryUnderTheBaseName()
    {
        if (!OperatingSystem.IsWindows()) return;
        var software = Software();
        software.HmiTextLists.ImportCreates = "Modes";
        var lists = Path.Combine(directory, "plant.hmi.yml");
        File.WriteAllText(lists, "TextLists:\n- Name: Modes\n");
        File.WriteAllText(Path.Combine(directory, "plant.TextLibrary.hmi.yml"), "TextLibrary:\n");

        var outcome = HmiTextListFiles.ImportLists(software, new HmiWriteRequestInfo { HmiName = "HMI_1", FilePath = lists });

        var import = Assert.Single(software.HmiTextLists.Imports);
        Assert.Equal("textlists", import.Name);
        Assert.Equal(new[] { "textlists.TextLibrary.hmi.yml", "textlists.hmi.yml" }, import.Files);
        Assert.Equal("TextLists:\n- Name: Modes\n", import.Lists);
        Assert.Contains("textList Modes (new)", outcome.AppliedProperties);
        Assert.Equal("textLists after import: States, Modes", outcome.AppliedProperties[^1]);
    }

    [Fact]
    public void Import_RefusesAMissingTextLibraryTheLibraryFileAndAFalseReturn()
    {
        if (!OperatingSystem.IsWindows()) return;
        var software = Software();
        var lists = Path.Combine(directory, "alone.hmi.yml");
        File.WriteAllText(lists, "TextLists:\n");
        var library = Path.Combine(directory, "pair.TextLibrary.hmi.yml");
        File.WriteAllText(library, "TextLibrary:\n");
        File.WriteAllText(Path.Combine(directory, "pair.hmi.yml"), "TextLists:\n");

        var missing = Assert.Throws<WorkerOperationException>(() => HmiTextListFiles.ImportLists(software, new HmiWriteRequestInfo { HmiName = "HMI_1", FilePath = lists }));
        var wrongFile = Assert.Throws<WorkerOperationException>(() => HmiTextListFiles.ImportLists(software, new HmiWriteRequestInfo { HmiName = "HMI_1", FilePath = library }));
        software.HmiTextLists.ImportResult = false;
        var refused = Assert.Throws<WorkerOperationException>(() => HmiTextListFiles.ImportLists(software, new HmiWriteRequestInfo { HmiName = "HMI_1", FilePath = Path.Combine(directory, "pair.hmi.yml") }));

        Assert.Equal(WorkerFailureCategories.TargetNotFound, missing.FailureCategory);
        Assert.Contains("alone.TextLibrary.hmi.yml", missing.Message);
        Assert.Equal(WorkerFailureCategories.ValidationError, wrongFile.FailureCategory);
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, refused.FailureCategory);
        Assert.Contains("returned false", refused.Message);
        Assert.NotNull(refused.HmiWriteOutcome);
    }

    [Fact]
    public void Import_OfASimaticMlFileStillNeedsTheSimaticMlMember()
    {
        if (!OperatingSystem.IsWindows()) return;
        var file = Path.Combine(directory, "States.xml");
        File.WriteAllText(file, "<Document />");

        var missing = Assert.Throws<WorkerOperationException>(() => HmiTextListFiles.ImportLists(Software(), new HmiWriteRequestInfo { HmiName = "HMI_1", FilePath = file }));

        Assert.Equal(WorkerFailureCategories.ApiMemberMissing, missing.FailureCategory);
        Assert.Contains(HmiTextListFiles.ImportMember, missing.Message);
    }
}
