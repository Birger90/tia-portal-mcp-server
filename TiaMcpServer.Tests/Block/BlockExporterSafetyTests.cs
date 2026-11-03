using TiaMcpServer.OpennessWorker.Openness.Block;
using Xunit;

namespace TiaMcpServer.Tests.Block;

public sealed class BlockExporterSafetyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExportXmlBundle_PortalLossEscapesInsteadOfReturningAFallback(bool companionFails)
    {
        var failure = new Siemens.Engineering.NonRecoverableException("closed");
        var companionCalls = 0;
        Assert.Same(failure, Assert.Throws<Siemens.Engineering.NonRecoverableException>(() =>
            BlockExporter.ExportXmlBundle("A/Blocks/Fixture", "Fixture",
                () => companionFails ? "<Document />" : throw failure,
                documents =>
                {
                    companionCalls++;
                    if (companionFails) throw failure;
                    documents.Add(new BlockImportDocument("Fixture.s7dcl", "Fixture.s7dcl", "fixture"));
                })));
        Assert.Equal(companionFails ? 1 : 0, companionCalls);
    }

    [Fact]
    public void ExportXmlBundle_FallsBackToCompanionsWhenXmlExportFails()
    {
        var companionCalls = 0;
        void ExportCompanions(ICollection<BlockImportDocument> documents)
        {
            companionCalls++;
            documents.Add(new BlockImportDocument("Mixer.s7dcl", "Mixer.s7dcl", "PRIVATE_COMPANION"));
        }

        var original = Console.Error;
        using var warnings = new StringWriter();
        string publicBundle;
        Console.SetError(warnings);
        try
        {
            publicBundle = BlockExporter.ExportXmlBundle("PLC_1/Blocks/Mixer", "Mixer",
                () => throw new InvalidOperationException("PRIVATE_XML_ERROR"), ExportCompanions);
        }
        finally
        {
            Console.SetError(original);
        }

        Assert.Contains("PRIVATE_COMPANION", publicBundle);
        Assert.DoesNotContain("PRIVATE_XML_ERROR", publicBundle);
        Assert.Equal(1, companionCalls);
        // D15: the fallback is no longer silent; the warning names the block and the cause, never the Siemens message.
        var text = warnings.ToString();
        Assert.Contains(TiaMcpServer.Contracts.Plc.Interface.SimaticMlExportNotes.NotExported("PLC_1/Blocks/Mixer", null), text);
        Assert.Contains("run compile_check and read again", text);
        Assert.DoesNotContain("PRIVATE_XML_ERROR", text);
    }

    [Fact]
    public void ExportXmlBundle_DiscardsPartialCompanionPackageWhenCompanionExportFails()
    {
        const string xml = "<Document><SW.Blocks.FC /></Document>";

        var bundle = BlockExporter.ExportXmlBundle(
            "PLC_1/Blocks/Mixer",
            "Mixer",
            () => xml,
            documents =>
            {
                documents.Add(new BlockImportDocument(
                    "Mixer.s7dcl", "Mixer.s7dcl", "PARTIAL_COMPANION"));
                throw new InvalidOperationException("later companion failed");
            });

        Assert.Equal(
            BlockBundleFormat.Compose(new[]
            {
                new BlockImportDocument("Mixer.xml", "Mixer.xml", xml)
            }),
            bundle);
        Assert.DoesNotContain("PARTIAL_COMPANION", bundle);
    }
}
