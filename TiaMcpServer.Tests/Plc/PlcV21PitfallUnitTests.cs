using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Interface;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.Plc;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

/// <summary>Roadmap row 22: the V21 pitfall guards, read flags, export ledger and construct verification.</summary>
public sealed class PlcV21PitfallUnitTests
{
    [Fact]
    public void Ledger_OpenedHere_CountsEveryExport_AndAWriteRestartsAtZero()
    {
        var ledger = new BlockExportLedger();
        ledger.Reset(openedHere: true);

        Assert.Equal((0, PlcBlockExportStateMethods.OpenedByThisServer), Read(ledger, "PLC_1", "Main"));
        ledger.RecordExport("PLC_1", "Main");
        ledger.RecordExport("plc_1", "MAIN");
        Assert.Equal((2, PlcBlockExportStateMethods.OpenedByThisServer), Read(ledger, "PLC_1", "Main"));
        Assert.Equal((0, PlcBlockExportStateMethods.OpenedByThisServer), Read(ledger, "PLC_2", "Main"));

        ledger.RecordWrite("PLC_1", "Main");
        ledger.RecordExport("PLC_1", "Main");
        Assert.Equal((1, PlcBlockExportStateMethods.WrittenByThisServer), Read(ledger, "PLC_1", "Main"));
    }

    [Fact]
    public void Ledger_Attached_IsUnknownUntilTheBlockIsWritten()
    {
        var ledger = new BlockExportLedger();
        ledger.Reset(openedHere: false);
        ledger.RecordExport("PLC_1", "Main");

        Assert.Equal(((int?)null, PlcBlockExportStateMethods.Unknown), Read(ledger, "PLC_1", "Main"));
        ledger.RecordWrite("PLC_1", "Main");
        ledger.RecordExport("PLC_1", "Main");
        Assert.Equal((1, PlcBlockExportStateMethods.WrittenByThisServer), Read(ledger, "PLC_1", "Main"));
    }

    [Fact]
    public void Ledger_ForgetCounts_KeepsTextualInterfaceFlags_AndResetClearsThem()
    {
        var ledger = new BlockExportLedger();
        ledger.Reset(openedHere: true);
        ledger.RecordTextualInterface("PLC_1", "Main", true);
        ledger.RecordExport("PLC_1", "Main");

        ledger.ForgetCounts();
        var state = ledger.Read("PLC_1", "Main");
        Assert.Null(state.ExportCount);
        Assert.True(state.TextualInterface);

        ledger.Reset(openedHere: true);
        Assert.Null(ledger.Read("PLC_1", "Main").TextualInterface);
    }

    private static (int? Count, string Basis) Read(BlockExportLedger ledger, string plc, string block)
    {
        var state = ledger.Read(plc, block);
        return (state.ExportCount, state.Basis);
    }

    [Fact]
    public void BundleText_FindsADocumentByExtension()
    {
        var bundle = "--- FILE: Main.xml ---\n<Document/>\n--- FILE: Main.s7dcl ---\nFUNCTION_BLOCK \"Main\"\n--- FILE: Main.s7res ---\nres\n";

        Assert.True(PlcBlockBundleText.TryGetDocument(bundle, ".s7dcl", out var name, out var content));
        Assert.Equal(("Main.s7dcl", "FUNCTION_BLOCK \"Main\"\n"), (name, content));
        Assert.True(PlcBlockBundleText.TryGetSimaticMl(bundle, out _, out var xml));
        Assert.Equal("<Document/>\n", xml);
        Assert.False(PlcBlockBundleText.TryGetDocument("<Document/>", ".xml", out _, out _));
    }

    [Theory]
    [InlineData("--- FILE: Main.s7dcl ---\nX\n--- FILE: Main.s7res ---\nY\n", true)]
    [InlineData("--- FILE: Main.xml ---\n<D/>\n--- FILE: Main.s7dcl ---\nX\n", false)]
    [InlineData("<Document/>", false)]
    [InlineData("--- FILE: Main.s7res ---\nY\n", false)]
    public void SimaticSdRoute_IsABundleWithAnS7dclAndNoXml(string content, bool expected)
        => Assert.Equal(expected, PlcV21Pitfalls.UsesSimaticSdRoute(content));

    [Fact]
    public void ReadWarnings_FlagHazardsAndATextualInterface()
    {
        var bundle = "--- FILE: Main.xml ---\n<Document/>\n--- FILE: Main.s7dcl ---\n{\n    S7_EditorMode := \"[SCL,TextualInterface]\"\n}\n"
            + "FUNCTION_BLOCK \"Main\"\n    VAR\n        tmr : _.TONR;\n    END_VAR\nEND_FUNCTION_BLOCK\n";

        var warnings = PlcV21PitfallReadWarnings.For("PLC_1/Main", bundle);

        Assert.Equal(2, warnings.Count);
        Assert.Contains("project_type_named_like_system_type 'tmr'", warnings[0]);
        Assert.Contains("textual interface", warnings[1]);
        Assert.Empty(PlcV21PitfallReadWarnings.For("PLC_1/Main", "--- FILE: Main.xml ---\n<Document/>\n"));
    }

    [Theory]
    [InlineData(PlcGuardDefinitions.S7dclImportMayAlter)]
    [InlineData(PlcGuardDefinitions.TextualInterfaceOverwrite)]
    [InlineData(PlcGuardDefinitions.SaveBeforeWrite)]
    public void PitfallGuards_AreInformative_AndSayReported(string id)
    {
        var definition = Assert.Single(PlcGuardDefinitions.Definitions, d => d.Id == id);
        Assert.Equal(WriteGuardSeverities.Info, definition.Severity);
        Assert.Contains("reported", definition.Description);
    }

    [Fact]
    public void ExportStateMethod_IsAnObservePlanningRead_GatedAsPlcWrite()
    {
        Assert.Equal(OperationCapability.Observe, OperationPolicyCatalog.GetCapability(PlcBlockExportStateMethods.GetExportState));
        Assert.Equal("plc_write", TiaMcpServer.Contracts.Project.ProjectCapabilityCatalog.OperationFor(PlcBlockExportStateMethods.GetExportState));
        Assert.DoesNotContain(PlcBlockExportStateMethods.GetExportState, PlcOperationCatalog.ReadOperationNames);
    }
}
