using Xunit;

namespace TiaMcpServer.Tests.Hmi;

/// <summary>
/// Static evidence for V21 pitfall 3 (docs/SupportedOperations/V21_PITFALLS.md; reported by rickgaiser/TiaMcp, not
/// verified on V21): exporting a graphic list and reading <c>HmiSystemDiagnosisControl.ScriptDiagnosisOverviewText</c>
/// are reported to crash TIA Portal V21. No HMI worker code may call an export directly, enumerate attributes generically
/// or name that property; a new path that needs one must skip the two and report "skipped: reported to crash TIA Portal V21".
/// The one export path is export_text_lists (<c>HmiTextListFiles</c>): it binds <c>Export</c> at runtime on the user text
/// lists of <c>HmiSoftware.HmiTextLists</c> only, never on graphic lists or system text lists.
/// </summary>
public sealed class HmiPitfallSourceTests
{
    [Theory]
    [InlineData("ScriptDiagnosisOverviewText")]
    [InlineData(".Export(")]
    [InlineData("GetAttributeInfos")]
    public void HmiWorkerCode_HasNoReportedCrashingPath(string forbidden)
    {
        var offenders = HmiWorkerSources()
            .Where(file => File.ReadAllText(file).Contains(forbidden, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToArray();

        Assert.True(offenders.Length == 0, $"'{forbidden}' appears in {string.Join(", ", offenders)}; see V21_PITFALLS.md pitfall 3.");
    }

    [Fact]
    public void GraphicLists_AreReadByNameOnly()
    {
        var uses = HmiWorkerSources().SelectMany(file => File.ReadAllLines(file)
            .Where(line => line.Contains("HmiGraphicLists", StringComparison.Ordinal))
            .Select(line => (File: Path.GetFileName(file), Line: line.Trim())))
            .ToArray();

        var use = Assert.Single(uses);
        Assert.Equal("HmiSoftwareInfoReader.cs", use.File);
        Assert.Contains("ReadNames(() => software.HmiGraphicLists, l => l.Name", use.Line);
    }

    [Fact]
    public void TextListFiles_AreTheOnlyExportAndNeverTouchGraphicOrSystemLists()
    {
        var binders = HmiWorkerSources().Where(file => File.ReadAllText(file).Contains("\"Export\"", StringComparison.Ordinal)).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "HmiClassicObjects.cs", "HmiTextListFiles.cs" }, binders);
        var source = File.ReadAllText(HmiWorkerSources().Single(file => Path.GetFileName(file) == "HmiTextListFiles.cs"));
        Assert.DoesNotContain("HmiGraphicList", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HmiSystemTextList", source, StringComparison.Ordinal);
        Assert.Contains("software.HmiTextLists", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The classic WinCC export (issue #28) is the second binder of Export: it must refuse a graphic list (the reported
    /// crash is a classic graphic list export) before any member is bound, and the catalog marks graphic lists unexportable.
    /// </summary>
    [Fact]
    public void ClassicExport_RefusesGraphicListsBeforeBindingExport()
    {
        var source = File.ReadAllText(HmiWorkerSources().Single(file => Path.GetFileName(file) == "HmiClassicObjects.cs"));
        var refusal = source.IndexOf("if (!kind.CanExport)", StringComparison.Ordinal);
        var bind = source.IndexOf("Bind(t.Item, \"Export\"", StringComparison.Ordinal);
        Assert.True(refusal > 0 && bind > refusal, "export_classic_objects must refuse a graphic list before it binds Export.");
        Assert.False(TiaMcpServer.Contracts.Hmi.HmiClassicKinds.Find(TiaMcpServer.Contracts.Hmi.HmiClassicKinds.GraphicLists)!.CanExport);
        Assert.StartsWith("skipped: reported to crash TIA Portal V21", TiaMcpServer.Contracts.Hmi.HmiClassicKinds.GraphicListExportSkipped);
    }

    private static string[] HmiWorkerSources()
    {
        var root = new DirectoryInfo(TiaMcpServer.Tests.TestSupport.RepositoryFiles.Root);
        // Openness/HmiExtensions (issue #29: audit classes, runtime settings, plant views) is HMI worker code too.
        var files = Directory.GetFiles(Path.Combine(root.FullName, "TiaMcpServer.OpennessWorker", "Openness", "Hmi"), "*.cs")
            .Concat(Directory.GetFiles(Path.Combine(root.FullName, "TiaMcpServer.OpennessWorker", "Openness", "HmiExtensions"), "*.cs")).ToArray();
        Assert.NotEmpty(files);
        return files;
    }
}
