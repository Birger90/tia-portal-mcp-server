using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.FakeWorker.Plc;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Worker;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Block;

/// <summary>The worker's Siemens-free SIMATIC SD pieces and the source shape of its Siemens calls (not live-tested).</summary>
public sealed class SimaticSdWorkerTests
{
    private static ParsedBlockImportBundle Bundle(string content) => BlockImportBundleParser.Parse("Main.s7dcl", content);

    [Fact]
    public void SdRoute_TakesExactlyOneS7dclAndItsS7res()
    {
        var fixture = SimaticSdFixtures.CookbookFcLad;
        Assert.Equal("MCPVerify_FC_LAD.s7dcl", BlockImportRouting.SelectSimaticSdDocument(Bundle(fixture.Bundle)).LogicalName);
        Assert.Equal(BlockImportRoute.SimaticSd, BlockImportRouting.SelectRoute(Bundle(fixture.Bundle)));

        string Refusal(string content) => Assert.Throws<WorkerOperationException>(() => BlockImportRouting.SelectSimaticSdDocument(Bundle(content))).Message;
        Assert.Contains("has a SimaticML (.xml)", Refusal($"--- FILE: Main.xml ---\n<Document/>\n{fixture.Bundle}"));
        Assert.Contains("exactly one .s7dcl", Refusal($"--- FILE: A.s7dcl ---\nx\n--- FILE: B.s7dcl ---\ny\n"));
        Assert.Contains("is not one of them", Refusal($"{fixture.Bundle}\n--- FILE: Other.s7res ---\nMultiLingualTexts:\n"));
    }

    [Fact]
    public void SdRoute_RequiresTheTargetsDeclaredName()
    {
        var s7dcl = BlockImportRouting.SelectSimaticSdDocument(Bundle(SimaticSdFixtures.CookbookFcLad.Bundle));
        BlockImportRouting.RequireDeclaredName(s7dcl, "MCPVerify_FC_LAD", "PLC_1/MCPVerify_FC_LAD");
        var error = Assert.Throws<WorkerOperationException>(() => BlockImportRouting.RequireDeclaredName(s7dcl, "Main", "PLC_1/Main"));
        Assert.Equal(WorkerFailureCategories.ValidationError, error.FailureCategory);
        Assert.Contains("never renames and never creates", error.Message);
    }

    private sealed class ResultMessage(string text)
    {
        public string Message { get; } = text;
    }

    private sealed class Result(params string[] texts)
    {
        public IEnumerable<object> Messages { get; } = texts.Select(t => new ResultMessage(t)).ToArray();
    }

    [Fact]
    public void ImportMessages_AreReadAtRuntimeAndForwardedAsControlledWarnings()
    {
        var messages = SimaticSdImportMessages.Read(new Result("Syntax Error: Unexpected input\r\n'{ S7_Language := \"LAD\" }'", "File Content has some discrepancies"));
        Assert.Equal(2, messages.Count);
        var warnings = SimaticSdImportMessages.AsWarnings(messages, "Failure");
        Assert.All(warnings, w => Assert.StartsWith(BlockImportDiagnosticSanitizer.SimaticSdMessagePrefix, w));
        Assert.Equal("SIMATIC SD import: Syntax Error: Unexpected input '{ S7_Language := \"LAD\" }'", warnings[0]);
        Assert.Equal(warnings, BlockImportDiagnosticSanitizer.SanitizeWarnings(warnings));
        Assert.Contains("no readable Messages member", SimaticSdImportMessages.Read(new object()).Single());
        Assert.Contains("state 'Failure' without messages", SimaticSdImportMessages.AsWarnings(Array.Empty<string>(), "Failure").Single());
        var longWarning = BlockImportDiagnosticSanitizer.SanitizeWarnings(new[] { BlockImportDiagnosticSanitizer.SimaticSdMessagePrefix + new string('x', 400) }).Single();
        Assert.Equal(256, longWarning.Length);
        Assert.Equal("Block import reported additional sanitized diagnostic information.", BlockImportDiagnosticSanitizer.SanitizeWarnings(new[] { "C:\\Temp\\tia-mcp-import-1\\x" }).Single());
    }

    [Fact]
    public void Staging_WritesExactlyOneBom()
    {
        var root = Path.Combine(Path.GetTempPath(), "tia-mcp-sd-stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = BlockImportStager.StageDocuments(root, Bundle("--- FILE: A.s7dcl ---\n\uFEFFFUNCTION \"A\" : Void\n--- FILE: A.s7res ---\nMultiLingualTexts:\n"));
            foreach (var path in paths)
            {
                var bytes = File.ReadAllBytes(path);
                Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
                Assert.NotEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Skip(3).Take(3));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ImportOutcomeEvidence_TreatsSdLikeXml()
    {
        Assert.Equal("sd", BlockUpdateOutcomeDecorator.NormalizeFormatOrNull("sd"));
        var refused = BlockUpdateOutcomeDecorator.Decorate(new WorkerOperationException(WorkerFailureCategories.ValidationError, "x"), "sd", importerEntered: false);
        Assert.Equal("not_applicable", refused.BlockImportOutcome!.TemporarySourceState);
        Assert.True(BlockImportOutcomeValidator.Validate(refused.BlockImportOutcome, "sd"));
        var synthesized = BlockImportOutcomeSynthesizer.Synthesize(WorkerDispatchState.NotSent, "sd");
        Assert.Equal("not_applicable", synthesized.TemporarySourceState);
        Assert.True(BlockImportOutcomeValidator.Validate(synthesized, "sd"));
    }

    private static string WorkerSource(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TiaMcpServer.slnx"))) directory = directory.Parent;
        return File.ReadAllText(Path.Combine(new[] { directory!.FullName, "TiaMcpServer.OpennessWorker", "Openness" }.Concat(segments).ToArray()));
    }

    [Fact]
    public void WorkerSource_UsesOnlyTheDocumentedSdMembersInTheCheckedOrder()
    {
        var importer = WorkerSource("Block", "BlockImporter.SimaticSd.cs");
        Assert.Contains("target.Group.Blocks.ImportFromDocuments(", importer);
        Assert.Contains("ImportDocumentOptions.Override);", importer);
        var safety = importer.IndexOf("Safety.PlcSafetyGate.RequireImportPermitted(", StringComparison.Ordinal);
        var hash = importer.IndexOf("BlockExporter.Export(project, blockPath, SourceFormatNames.Sd));", StringComparison.Ordinal);
        var name = importer.IndexOf("BlockImportRouting.RequireDeclaredName(", StringComparison.Ordinal);
        Assert.True(name > 0 && name < safety && safety < hash, "declared name, safety rule, then the sd hash before the coordinator runs the import");

        var create = WorkerSource("Block", "PlcBlockEditService.Documents.cs");
        var gate = create.IndexOf("RequireImportPermitted(", StringComparison.Ordinal);
        Assert.True(gate > 0 && gate < create.IndexOf("target.Group.Blocks.ImportFromDocuments(", StringComparison.Ordinal));
        Assert.Contains("ImportDocumentOptions.Override", create);
        Assert.Contains("SimaticSdFileText.Encode", create);

        var exporter = WorkerSource("Block", "BlockExporter.cs");
        Assert.Contains("target.Block!.ExportAsDocuments(new DirectoryInfo(tempDir), target.DocumentName);", exporter);
        Assert.Contains("SimaticSdFileText.Decode(File.ReadAllBytes(file.FullName))", exporter);
        // DocumentImportResultForBlocks.Messages is not in the stubs: read only by reflection.
        Assert.DoesNotContain("result.Messages", importer + create);
    }
}
