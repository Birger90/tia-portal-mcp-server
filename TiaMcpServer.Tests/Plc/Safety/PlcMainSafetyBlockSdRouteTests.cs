using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Interface;
using TiaMcpServer.Contracts.Plc.Safety;
using TiaMcpServer.Contracts.Plc.SimaticSd;
using TiaMcpServer.Contracts.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Plc.Safety;

/// <summary>
/// The Siemens-free main safety block route (live workaround L4, TIA Portal V21, 2026-11-02) against scripted steps: the order
/// of the steps, the stop before the main block on a compile error or a missing SimaticML, and the cleanup of the temporary
/// block on every path. The steps model shapes only; they are no evidence of Siemens behavior.
/// </summary>
public sealed class PlcMainSafetyBlockSdRouteTests
{
    private const string Header = "{\n    S7_BlockNumber := \"1\";\n    S7_Optimized := \"TRUE\";\n    S7_PreferredLanguage := \"FBD\";\n    S7_Safety := \"TRUE\";\n    S7_Version := \"0.1\"\n}\n";

    private const string Body = "FUNCTION_BLOCK \"Main_Safety_RTG1\"\n    VAR_INPUT\n        Start : Bool;\n    END_VAR\n\n    { S7_Language := \"FBD\" }\n    NETWORK\n        Coil( #Start )\n    END_NETWORK\nEND_FUNCTION_BLOCK\n";

    private const string Document = Header + Body;

    private const string TemporaryXml = "<Document>\r\n  <SW.Blocks.FB ID=\"0\">\r\n    <AttributeList>\r\n      <Interface />\r\n      <Name>TEMP</Name>\r\n      <Number>2</Number>\r\n    </AttributeList>\r\n  </SW.Blocks.FB>\r\n</Document>\r\n";

    private sealed class Steps : IPlcMainSafetyBlockSdRouteSteps<string>
    {
        public List<string> Calls { get; } = new();

        public HashSet<string> Existing { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? ImportedS7dcl { get; private set; }

        public string? ImportedMain { get; private set; }

        public Func<string, Exception?> FailImportTemporary { get; set; } = _ => null;

        public bool CreatesOnFailedImport { get; set; }

        public CompileCheckReport Compile { get; set; } = new() { Scope = "block", OverallState = "Success" };

        public bool ExportMissing { get; set; }

        public Exception? FailMain { get; set; }

        public Exception? FailDelete { get; set; }

        public Exception? Fatal { get; set; }

        public bool TemporaryExists(string name)
        {
            Calls.Add("exists");
            return Existing.Contains(name);
        }

        public void ImportTemporary(string name, string s7dcl, string? s7res)
        {
            Calls.Add("importTemporary");
            if (Fatal is not null) throw Fatal;
            if (FailImportTemporary(name) is { } failure)
            {
                if (CreatesOnFailedImport) Existing.Add(name);
                throw failure;
            }

            ImportedS7dcl = s7dcl;
            Existing.Add(name);
        }

        public CompileCheckReport CompileTemporary(string name)
        {
            Calls.Add("compile");
            return Compile;
        }

        public string? ExportTemporarySimaticMl(string name, out string? reason)
        {
            Calls.Add("export");
            reason = ExportMissing ? "the block is inconsistent" : null;
            return ExportMissing ? null : TemporaryXml.Replace("TEMP", name);
        }

        public PlcMainSafetyBlockIdentity MainIdentity()
        {
            Calls.Add("identity");
            return new PlcMainSafetyBlockIdentity(1, false, PlcMainSafetyBlockSdRoute.IdentitySources.SimaticMlExport);
        }

        public string ImportMain(string simaticMl)
        {
            Calls.Add("importMain");
            if (FailMain is not null) throw FailMain;
            ImportedMain = simaticMl;
            return "imported";
        }

        public void DeleteTemporary(string name)
        {
            Calls.Add("delete");
            if (FailDelete is not null) throw FailDelete;
            Existing.Remove(name);
        }

        public bool IsFatal(Exception exception) => exception is OperationCanceledException;

        public string CategoryOf(Exception exception) => exception is SimaticSdException sd ? sd.Category : WorkerFailureCategories.EngineeringRejected;
    }

    private static (string Result, PlcMainSafetyBlockRouteInfo Info, List<string> Warnings) Run(Steps steps, string document = Document)
        => PlcMainSafetyBlockSdRoute.Run(steps, "update_block_logic", "PLC_F1/Blocks/Main_Safety_RTG1", "Main_Safety_RTG1", "PLC_F1/Blocks",
            new[] { "F-Ablaufgruppe 1" }, document, null);

    [Fact]
    public void TemporaryName_IsDeterministicPrefixedSafeAndFitsTheBlockNameLimit()
    {
        var name = PlcMainSafetyBlockSdRoute.TemporaryName("Main_Safety_RTG1", Document, null);

        Assert.Matches("^zzMcpTmp_Main_Safety_RTG1_[0-9a-f]{8}$", name);
        Assert.Equal(name, PlcMainSafetyBlockSdRoute.TemporaryName("Main_Safety_RTG1", Document, null));
        Assert.NotEqual(name, PlcMainSafetyBlockSdRoute.TemporaryName("Main_Safety_RTG1", Document + " ", null));
        Assert.Matches("^zzMcpTmp_F_Block_1_[0-9a-f]{8}$", PlcMainSafetyBlockSdRoute.TemporaryName("F-Block 1", Document, null));
        Assert.Equal(PlcMainSafetyBlockSdRoute.MaxBlockNameLength, PlcMainSafetyBlockSdRoute.TemporaryName(new string('A', 200), Document, null).Length);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void TemporaryDocument_RenamesDropsTheNumberKeepsSafety_AndKeepsTheLineEndings(string newline)
    {
        var source = "﻿" + Document.Replace("\n", newline);

        var (text, notes) = PlcMainSafetyBlockSdRoute.TemporaryDocument(source, "zzMcpTmp_Main_Safety_RTG1_0a1b2c3d");

        var expected = "﻿" + (Header.Replace("    S7_BlockNumber := \"1\";\n", string.Empty) + Body)
            .Replace("FUNCTION_BLOCK \"Main_Safety_RTG1\"", "FUNCTION_BLOCK \"zzMcpTmp_Main_Safety_RTG1_0a1b2c3d\"").Replace("\n", newline);
        Assert.Equal(expected, text);
        Assert.Contains(notes, n => n.Contains("S7_BlockNumber \"1\" was removed"));
        Assert.DoesNotContain(notes, n => n.Contains("S7_Safety"));
        Assert.Equal("zzMcpTmp_Main_Safety_RTG1_0a1b2c3d", SimaticSdDocument.Parse(text).BlockName);
        Assert.True(SimaticSdDocument.Parse(text).IsFailSafe);
    }

    [Fact]
    public void TemporaryDocument_AddsS7SafetyWhenTheHeaderHasNone_OrAHeaderWhenThereIsNone()
    {
        var withoutSafety = Document.Replace("    S7_Safety := \"TRUE\";\n", string.Empty);
        var (text, notes) = PlcMainSafetyBlockSdRoute.TemporaryDocument(withoutSafety, "zzMcpTmp_X_00000000");
        Assert.True(SimaticSdDocument.Parse(text).IsFailSafe);
        Assert.Contains(notes, n => n.Contains("S7_Safety := \"TRUE\" was added"));

        var (bare, _) = PlcMainSafetyBlockSdRoute.TemporaryDocument(Body, "zzMcpTmp_X_00000000");
        Assert.StartsWith("{ S7_Safety := \"TRUE\" }\nFUNCTION_BLOCK \"zzMcpTmp_X_00000000\"", bare);
        Assert.True(SimaticSdDocument.Parse(bare).IsFailSafe);

        var keptFalse = Document.Replace("S7_Safety := \"TRUE\"", "S7_Safety := \"FALSE\"");
        Assert.Contains("S7_Safety := \"FALSE\"", PlcMainSafetyBlockSdRoute.TemporaryDocument(keptFalse, "zzMcpTmp_X_00000000").S7dcl);
    }

    [Fact]
    public void Run_ImportsCompilesExportsTransplantsImportsTheMainBlockAndDeletesTheTemporaryBlock()
    {
        var steps = new Steps();

        var (result, info, warnings) = Run(steps);

        Assert.Equal("imported", result);
        Assert.Empty(warnings);
        // K36: deleting the temporary block before the main import terminated TIA Portal V21 live; it stays until afterwards.
        Assert.Equal(new[] { "exists", "importTemporary", "compile", "export", "identity", "importMain", "delete", "exists" }, steps.Calls);
        Assert.Equal(PlcMainSafetyBlockSdRoute.Route, info.Route);
        Assert.Equal(PlcMainSafetyBlockSdRoute.MainImports.SimaticMl, info.MainImport);
        Assert.Equal(PlcMainSafetyBlockSdRoute.TemporaryStates.Deleted, info.TemporaryBlockState);
        Assert.Equal("PLC_F1/Blocks/" + info.TemporaryBlockName, info.TemporaryBlockPath);
        Assert.Equal((1, (bool?)false), (info.MainNumber!.Value, info.MainAutoNumber));
        Assert.Contains(info.TemporaryBlockName, steps.ImportedS7dcl);
        var main = SimaticMlBlockIdentity.Read(steps.ImportedMain!);
        Assert.Equal(("Main_Safety_RTG1", "1", "false"), (main.Name, main.Number, main.AutoNumber));
        Assert.Contains("<AutoNumber>false</AutoNumber>\r\n      <Interface />", steps.ImportedMain);
        Assert.Equal(6, info.Steps.Count);
    }

    [Fact]
    public void Run_ATemporaryCompileError_StopsBeforeTheMainBlock_DeletesTheTemporaryBlock_AndNamesTheMessages()
    {
        var steps = new Steps
        {
            Compile = new CompileCheckReport
            {
                Scope = "block", OverallState = "Error", TotalErrorCount = 1,
                Plcs = { new PlcCompileInfo { PlcName = "PLC_F1", State = "Error", ErrorCount = 1, Messages = { new CompileMessageInfo { Description = "Network 1: invalid operand", Path = "zz", Severity = "Error" } } } },
            },
        };

        var failure = Assert.Throws<PlcMainSafetyBlockRouteException>(() => Run(steps));

        Assert.DoesNotContain("importMain", steps.Calls);
        Assert.DoesNotContain("export", steps.Calls);
        Assert.Contains("delete", steps.Calls);
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, failure.Category);
        Assert.Contains("did not compile", failure.Message);
        Assert.Contains("Network 1: invalid operand", failure.Message);
        Assert.Contains("was not changed", failure.Message);
        Assert.Contains("was deleted", failure.Message);
        Assert.Equal(PlcMainSafetyBlockSdRoute.MainImports.NotStarted, failure.Info.MainImport);
        Assert.Equal(PlcMainSafetyBlockSdRoute.TemporaryStates.Deleted, failure.Info.TemporaryBlockState);
        Assert.Equal(1, failure.Info.TemporaryCompile!.ErrorCount);
    }

    [Fact]
    public void Run_AMissingSimaticMlExport_StopsBeforeTheMainBlock()
    {
        var steps = new Steps { ExportMissing = true };

        var failure = Assert.Throws<PlcMainSafetyBlockRouteException>(() => Run(steps));

        Assert.DoesNotContain("importMain", steps.Calls);
        Assert.Contains("exported no SimaticML", failure.Message);
        Assert.Contains("the block is inconsistent", failure.Message);
        Assert.Equal(PlcMainSafetyBlockSdRoute.TemporaryStates.Deleted, failure.Info.TemporaryBlockState);
    }

    [Fact]
    public void Run_AnExistingTemporaryName_IsRefusedBeforeAnything()
    {
        var steps = new Steps();
        steps.Existing.Add(PlcMainSafetyBlockSdRoute.TemporaryName("Main_Safety_RTG1", Document, null));

        var failure = Assert.Throws<PlcMainSafetyBlockRouteException>(() => Run(steps));

        Assert.Equal(new[] { "exists" }, steps.Calls);
        Assert.Equal(WorkerFailureCategories.ValidationError, failure.Category);
        Assert.Contains("already exists", failure.Message);
        Assert.Equal(PlcMainSafetyBlockSdRoute.TemporaryStates.NotCreated, failure.Info.TemporaryBlockState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_AFailedTemporaryImport_CleansUpOnlyWhatItCreated(bool created)
    {
        var steps = new Steps { FailImportTemporary = _ => new InvalidOperationException("Syntax Error"), CreatesOnFailedImport = created };

        var failure = Assert.Throws<PlcMainSafetyBlockRouteException>(() => Run(steps));

        Assert.Equal(created, steps.Calls.Contains("delete"));
        Assert.Equal(created ? PlcMainSafetyBlockSdRoute.TemporaryStates.Deleted : PlcMainSafetyBlockSdRoute.TemporaryStates.NotCreated, failure.Info.TemporaryBlockState);
        Assert.Contains("Syntax Error", failure.Message);
        Assert.Equal(WorkerFailureCategories.EngineeringRejected, failure.Category);
    }

    [Fact]
    public void Run_AFailedMainImport_IsReported_AndTheTemporaryBlockIsDeleted()
    {
        var steps = new Steps { FailMain = new InvalidOperationException("Block import did not complete.") };

        var failure = Assert.Throws<PlcMainSafetyBlockRouteException>(() => Run(steps));

        Assert.Equal(PlcMainSafetyBlockSdRoute.MainImports.Failed, failure.Info.MainImport);
        Assert.Equal(PlcMainSafetyBlockSdRoute.TemporaryStates.Deleted, failure.Info.TemporaryBlockState);
        Assert.Contains("may have been changed", failure.Message);
        Assert.IsType<InvalidOperationException>(failure.InnerException);
    }

    [Fact]
    public void Run_ATemporaryBlockThatCannotBeDeleted_IsNamedInAWarning_AndInTheEvidence()
    {
        var steps = new Steps { FailDelete = new InvalidOperationException("locked") };

        var (_, info, warnings) = Run(steps);

        Assert.Equal(PlcMainSafetyBlockSdRoute.MainImports.SimaticMl, info.MainImport);
        Assert.Equal(PlcMainSafetyBlockSdRoute.TemporaryStates.Left, info.TemporaryBlockState);
        Assert.Equal("locked", info.CleanupProblem);
        var warning = Assert.Single(warnings);
        Assert.Contains(info.TemporaryBlockPath, warning);
        Assert.Contains("delete_block", warning);

        var failing = new Steps { FailDelete = new InvalidOperationException("locked"), ExportMissing = true };
        var failure = Assert.Throws<PlcMainSafetyBlockRouteException>(() => Run(failing));
        Assert.Contains("could NOT be deleted (locked)", failure.Message);
        Assert.Contains(failure.Info.TemporaryBlockName, failure.Message);
    }

    [Fact]
    public void Run_AFatalException_EscapesWithoutCleanup()
    {
        var steps = new Steps { Fatal = new OperationCanceledException("portal lost") };

        Assert.Throws<OperationCanceledException>(() => Run(steps));
        Assert.DoesNotContain("delete", steps.Calls);
    }

    [Fact]
    public void Run_ADocumentWithoutDeclaration_IsAValidationError_AndNothingRuns()
    {
        var steps = new Steps();

        var failure = Assert.Throws<PlcMainSafetyBlockRouteException>(() => Run(steps, "{ S7_Safety := \"TRUE\" }\n"));

        Assert.Empty(steps.Calls);
        Assert.Equal(WorkerFailureCategories.ValidationError, failure.Category);
    }

    [Fact]
    public void MainBlockGroups_NamesOnlyGroupsWhoseMainSafetyBlockIsTheBlock()
    {
        var links = new[]
        {
            new PlcMainSafetyLink("F-Ablaufgruppe 1", "Main_Safety_RTG1", "Main_Safety_RTG1_DB"),
            new PlcMainSafetyLink("F-Ablaufgruppe 2", "Other", "Main_Safety_RTG1"),
        };

        Assert.Equal(new[] { "F-Ablaufgruppe 1" }, PlcMainSafetyBlockSdRoute.MainBlockGroups(links, "main_safety_rtg1"));
        Assert.Empty(PlcMainSafetyBlockSdRoute.MainBlockGroups(links, "Main_Safety_RTG1_DB"));
    }
}
