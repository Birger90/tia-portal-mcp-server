using System.ComponentModel;
using System.Reflection;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Safety;
using TiaMcpServer.Contracts.Plc.SimaticSd;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.FakeWorker.Plc;
using TiaMcpServer.Plc;
using Xunit;

namespace TiaMcpServer.Tests.Plc.SimaticSd;

/// <summary>
/// The Siemens-free SIMATIC SD model against real V21 exports (fixtures from 1748477582/tia-portal-openness-mcp and
/// HonzaT/BetterTIA, see SimaticSdFixtures) and the Siemens V21 simatic-sd skill rules (§4-§7, §10).
/// </summary>
public sealed class SimaticSdDocumentTests
{
    private static SimaticSdDocument Doc(SimaticSdFixture fixture) => SimaticSdDocument.Parse(fixture.S7dcl);

    // ------------------------------------------------------------------ parsing

    [Fact]
    public void Parse_CookbookFb_ReadsTheMultiLinePragmasHeaderAndNetworks()
    {
        var document = Doc(SimaticSdFixtures.CookbookFbLadV3);

        Assert.True(document.HasBom);
        Assert.Equal("\r\n", document.Newline);
        Assert.Equal("FUNCTION_BLOCK", document.BlockKind);
        Assert.Equal("MCPVerify_FB_LAD_v3", document.BlockName);
        Assert.Equal("LAD", document.PreferredLanguage);
        Assert.False(document.IsFailSafe);
        Assert.Equal(new[] { "S7_BlockComment", "S7_BlockNumber", "S7_BlockTitle", "S7_Optimized", "S7_PreferredLanguage", "S7_Version" },
            document.HeaderPragma!.Entries.Select(e => e.Key));
        Assert.Equal(4, document.Networks.Count);
        var first = document.Networks[0];
        Assert.True(first.Pragma!.IsMultiLine);
        Assert.Equal(("LAD", "pragma"), (first.Language, first.LanguageSource));
        Assert.Equal("MLC_463", first.Pragma.Get("S7_NetworkTitle"));
        Assert.Equal("MLC_4zs", first.Pragma.Get("S7_NetworkComment"));
        Assert.Equal(first.Pragma.EndLine + 1, first.NetworkLine);
        // The inner S7_Templates pragma is part of the body.
        Assert.Contains("{ S7_Templates := \"time_type := Time\" }", first.Code);
        Assert.Equal(SimaticSdFixtures.CookbookFbLadV3.S7dcl, document.Render());
    }

    [Fact]
    public void Parse_MixedBlock_ReadsSingleLinePragmasCommentsAndAnSclNetwork()
    {
        var document = Doc(SimaticSdFixtures.CookbookMixed);

        Assert.Equal(new[] { "LAD", "LAD", "SCL", "LAD" }, document.Networks.Select(n => n.Language));
        Assert.All(document.Networks, n => Assert.False(n.Pragma!.IsMultiLine));
        Assert.Equal("#Sum := #Va + #Vb;", document.Networks[2].Code);
        // The inline template form stays intact (reported call form).
        Assert.Contains("#ton1.TON{ time_type := Time }( PT := T#500ms, ET => )", document.Networks[1].Code);
        Assert.Equal(SimaticSdFixtures.CookbookMixed.S7dcl, document.Render());
    }

    [Fact]
    public void Parse_FailSafeFbd_DetectsS7SafetyAndKeepsInnerPragmas()
    {
        var document = Doc(SimaticSdFixtures.BetterTiaFbSafety);

        Assert.True(document.IsFailSafe);
        Assert.False(document.HasBom);
        Assert.Equal("\n", document.Newline);
        Assert.Equal("FBD", document.PreferredLanguage);
        Assert.Equal(9, document.Networks.Count);
        Assert.All(document.Networks, n => Assert.Equal("FBD", n.Language));
        Assert.Contains("f_user_card := \"1\";", document.Networks[3].Code);
        Assert.Contains("#EStop.ESTOP1(", document.Networks[3].Code);
        Assert.Null(document.Networks[8].Pragma!.Get("S7_NetworkTitle"));
        Assert.True(PlcSafetyPolicy.DeclaresSimaticSdSafety(SimaticSdFixtures.BetterTiaFbSafety.Bundle));
        Assert.True(PlcSafetyPolicy.DeclaresFailSafe(SimaticSdFixtures.BetterTiaFbSafety.S7dcl));
        Assert.False(PlcSafetyPolicy.DeclaresSimaticSdSafety(SimaticSdFixtures.BetterTiaFcBranches.Bundle));
    }

    [Fact]
    public void Parse_BranchesAndFbd_KeepsWireLabelsAndMixedLanguages()
    {
        var document = Doc(SimaticSdFixtures.BetterTiaFcBranches);

        Assert.Equal(new[] { "LAD", "LAD", "FBD", "LAD", "LAD" }, document.Networks.Select(n => n.Language));
        Assert.Contains("END_RUNG wire#w2", document.Networks[0].Code);
        Assert.Contains("RUNG \"Sensor_A\"", document.Networks[2].Code);
        Assert.Contains("O( in2 := \"Sensor_B\" )", document.Networks[2].Code);
    }

    [Fact]
    public void Parse_NetworkWithoutPragma_DetectsTheLanguageAsTheSiemensSkill()
    {
        var document = SimaticSdDocument.Parse("FUNCTION \"F\" : Void\n    NETWORK\n        RUNG wire#powerrail\n            Coil( #Q )\n        END_RUNG\n    END_NETWORK\n"
            + "    NETWORK\n        RUNG \"Tag\"\n            Coil( #Q )\n        END_RUNG\n    END_NETWORK\n    NETWORK\n        #a := 1;\n    END_NETWORK\nEND_FUNCTION\n");

        Assert.Equal(new[] { ("LAD", "detected"), ("FBD", "detected"), ("SCL", "detected") }, document.Networks.Select(n => (n.Language!, n.LanguageSource!)));
        Assert.All(document.Networks, n => Assert.Null(n.Pragma));
    }

    [Theory]
    [InlineData("    NETWORK\n    NETWORK\n    END_NETWORK\n")]
    [InlineData("    END_NETWORK\n")]
    [InlineData("    NETWORK\n        #a := 1;\n")]
    public void Parse_UnpairedNetworks_IsAValidationError(string body)
    {
        var error = Assert.Throws<SimaticSdException>(() => SimaticSdDocument.Parse("FUNCTION \"F\" : Void\n" + body + "END_FUNCTION\n"));
        Assert.Equal("validation_error", error.Category);
    }

    [Theory]
    [InlineData("{ S7_Language := \"LAD\" }\nNETWORK\nRUNG wire#powerrail\nEND_RUNG\nEND_NETWORK", "LAD")]
    [InlineData("RUNG wire#powerrail\n  Contact( #a )\nEND_RUNG", "LAD")]
    [InlineData("RUNG 1\n  Coil( #a )\nEND_RUNG", "FBD")]
    [InlineData("RUNG \"DB\".x\n  Coil( #a )\nEND_RUNG", "FBD")]
    [InlineData("#x := #a + #b;", "SCL")]
    [InlineData("RUNG wire#powerrail\nEND_RUNG\nRUNG 1\nEND_RUNG", null)]
    [InlineData("RUNG #Start\n  Coil( #a )\nEND_RUNG", null)]
    [InlineData("   ", null)]
    public void DetectLanguage_FollowsSection5OfTheSiemensSkill(string code, string? expected)
        => Assert.Equal(expected, SimaticSdText.DetectLanguage(code));

    // ------------------------------------------------------------------ caller code (§6, §10)

    [Fact]
    public void NormalizeCode_StripsOnlyTheOuterWrappersAndTheLanguagePragma()
    {
        var code = SimaticSdText.NormalizeNetworkCode("""
            {
                S7_Language := "LAD"
            }
            NETWORK
                RUNG wire#powerrail
                    Contact( #Trig )

                    { S7_Templates := "time_type := Time" }
                    #tonInst.TON(
                        pt := T#500ms,
                        et =>
                    )
                END_RUNG
            END_NETWORK
            """);

        Assert.Equal("LAD", code.DeclaredLanguage);
        Assert.Empty(code.ExtraPragmaEntries);
        Assert.Equal("RUNG wire#powerrail", code.BodyLines[0]);
        Assert.Equal("    { S7_Templates := \"time_type := Time\" }", code.BodyLines[2]);
        Assert.DoesNotContain(code.BodyLines, l => l.Trim().Length == 0);
        Assert.Equal("END_RUNG", code.BodyLines[^1]);
    }

    [Fact]
    public void NormalizeCode_AcceptsRungBodiesSclAndTheSingleLinePragma()
    {
        Assert.Null(SimaticSdText.NormalizeNetworkCode("RUNG wire#powerrail\n  Coil( #Q )\nEND_RUNG").DeclaredLanguage);
        Assert.Equal(new[] { "#x := #a + #b;" }, SimaticSdText.NormalizeNetworkCode("\n  #x := #a + #b;\n").BodyLines);
        var pragma = SimaticSdText.NormalizeNetworkCode("{ S7_Language := \"FBD\" }\nRUNG \"Tag\"\n  Coil( #Q )\nEND_RUNG");
        Assert.Equal("FBD", pragma.DeclaredLanguage);
        Assert.Equal(3, pragma.BodyLines.Count);
        // A stray S7_Language pragma line in the body is stripped (§6).
        Assert.Equal(new[] { "#x := 1;" }, SimaticSdText.NormalizeNetworkCode("NETWORK\n{ S7_Language := \"SCL\" }\n#x := 1;\nEND_NETWORK").BodyLines);
        // Other keys of the outer pragma are kept for the network's pragma.
        Assert.Equal(new[] { "S7_Custom" }, SimaticSdText.NormalizeNetworkCode("{ S7_Language := \"LAD\"; S7_Custom := \"x\" }\nNETWORK\nRUNG wire#powerrail\nEND_RUNG\nEND_NETWORK")
            .ExtraPragmaEntries.Select(e => e.Key));
    }

    [Theory]
    [InlineData("NETWORK\nRUNG wire#powerrail\nEND_RUNG\nEND_NETWORK\nNETWORK\n#a := 1;\nEND_NETWORK", "exactly one network")]
    [InlineData("NETWORK\nRUNG wire#powerrail\nEND_RUNG", "NETWORK without END_NETWORK")]
    [InlineData("RUNG wire#powerrail\nEND_RUNG\nEND_NETWORK", "END_NETWORK without NETWORK")]
    [InlineData("RUNG wire#powerrail\n  Coil( #Q )", "has no END_RUNG")]
    [InlineData("RUNG wire#powerrail\nRUNG wire#w1\nEND_RUNG\nEND_RUNG", "opens before")]
    [InlineData("END_RUNG", "has no RUNG")]
    [InlineData("NETWORK RUNG wire#powerrail END_RUNG END_NETWORK", "on a line of their own")]
    [InlineData("NETWORK\n#a := 1;\nEND_NETWORK\n#b := 2;", "continues after END_NETWORK")]
    [InlineData("// comment\nNETWORK\n#a := 1;\nEND_NETWORK", "only a pragma")]
    [InlineData("{ S7_Language := \"LAD\"; S7_NetworkTitle := \"Pumps\" }\nNETWORK\nRUNG wire#powerrail\nEND_RUNG\nEND_NETWORK", "set_network_text")]
    [InlineData("NETWORK\nEND_NETWORK", "no network content")]
    [InlineData("NETWORK\n{ S7_Language := \"LAD\"; S7_X := \"1\" }\nRUNG wire#powerrail\nEND_RUNG\nEND_NETWORK", "belongs to the network's own pragma")]
    public void NormalizeCode_RefusesOnlyWhatWouldCorruptTheDocument(string code, string message)
    {
        var error = Assert.Throws<SimaticSdException>(() => SimaticSdText.NormalizeNetworkCode(code));
        Assert.Equal("validation_error", error.Category);
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void NormalizeCode_IgnoresKeywordsInOperandsStringsAndComments()
    {
        var code = SimaticSdText.NormalizeNetworkCode("RUNG wire#powerrail\n  Contact( #RUNG )\n  Coil( \"NETWORK\" ) // END_RUNG\n  (* RUNG *)\nEND_RUNG");
        Assert.Equal(5, code.BodyLines.Count);
        // LAD semantics are never judged: an unknown instruction passes (TIA Portal decides).
        Assert.Single(SimaticSdText.NormalizeNetworkCode("RUNG wire#powerrail\n  NoSuchBox( #x )\nEND_RUNG").BodyLines, l => l.Contains("NoSuchBox"));
    }

    // ------------------------------------------------------------------ edits (§7)

    private static readonly SimaticSdNetworkCode Rung = SimaticSdText.NormalizeNetworkCode("RUNG wire#powerrail\n    Contact( #New )\n    Coil( #Out )\nEND_RUNG");

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    [InlineData(null, 5)]
    public void Insert_PlacesTheNetworkWithItsPragmaAtThePosition(int? position, int expectedNumber)
    {
        var original = Doc(SimaticSdFixtures.CookbookFbLadV3);
        var edited = SimaticSdDocument.Parse(original.InsertNetwork(Rung, "LAD", position));

        Assert.Equal(5, edited.Networks.Count);
        var inserted = edited.Networks[expectedNumber - 1];
        Assert.Equal("{ S7_Language := \"LAD\" }", edited.Lines[inserted.Pragma!.StartLine].Trim());
        Assert.Equal(inserted.Pragma.EndLine + 1, inserted.NetworkLine);
        Assert.Equal(Rung.Normalized, SimaticSdText.NormalizeForComparison(inserted.BodyLines));
        Assert.Equal("    NETWORK", edited.Lines[inserted.NetworkLine]);
        Assert.StartsWith("        RUNG", edited.Lines[inserted.NetworkLine + 1]);
        // Every other network, its pragma included, is unchanged; BOM and CRLF are kept.
        Assert.Equal(original.Networks.Select(n => n.Code), edited.Networks.Where(n => n.Number != expectedNumber).Select(n => n.Code));
        Assert.Equal(original.Networks.Select(n => string.Join("|", n.Pragma!.Lines)), edited.Networks.Where(n => n.Number != expectedNumber).Select(n => string.Join("|", n.Pragma!.Lines)));
        Assert.True(edited.HasBom);
        Assert.DoesNotContain("\n", edited.Render().Replace("\r\n", string.Empty));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Insert_OutsideTheNetworks_IsTargetNotFound(int position)
        => Assert.Equal("target_not_found", Assert.Throws<SimaticSdException>(() => Doc(SimaticSdFixtures.CookbookFbLadV3).InsertNetwork(Rung, "LAD", position)).Category);

    [Fact]
    public void Insert_IntoABlockWithoutNetworks_GoesBeforeTheEndKeyword()
    {
        var empty = SimaticSdDocument.Parse("﻿{\n    S7_PreferredLanguage := \"LAD\"\n}\nFUNCTION \"F\" : Void\n    VAR_TEMP\n        x : Bool;\n    END_VAR\nEND_FUNCTION\n");
        var edited = SimaticSdDocument.Parse(empty.InsertNetwork(SimaticSdText.NormalizeNetworkCode("#x := TRUE;"), "SCL", null));

        Assert.Equal(("SCL", "#x := TRUE;"), (edited.Networks.Single().Language!, edited.Networks.Single().Code));
        Assert.Equal("END_FUNCTION", edited.Lines[^1]);
        Assert.True(edited.HasBom);
    }

    [Fact]
    public void Replace_KeepsEveryPragmaKeyAndChangesOnlyTheLanguageValue()
    {
        var original = Doc(SimaticSdFixtures.CookbookFbLadV3);
        var edited = SimaticSdDocument.Parse(original.ReplaceNetwork(2, SimaticSdText.NormalizeNetworkCode("#OutPulse := #PulseBit;"), "SCL"));

        var network = edited.Networks[1];
        Assert.Equal(("SCL", "MLC_3qd", "MLC_4iu"), (network.Language!, network.Pragma!.Get("S7_NetworkTitle")!, network.Pragma.Get("S7_NetworkComment")!));
        Assert.Equal(original.Networks[1].Pragma!.Lines.Select(l => l.Replace("\"LAD\"", "\"SCL\"")), network.Pragma.Lines);
        Assert.Equal("#OutPulse := #PulseBit;", network.Code);
        Assert.Equal(4, edited.Networks.Count);
        Assert.Equal(original.Networks[0].Code, edited.Networks[0].Code);
    }

    [Fact]
    public void Replace_WithoutLanguage_KeepsThePragmaByteIdentical()
    {
        var original = Doc(SimaticSdFixtures.BetterTiaFbSafety);
        var edited = SimaticSdDocument.Parse(original.ReplaceNetwork(1, SimaticSdText.NormalizeNetworkCode("RUNG #Start\n    Coil( #Enable )\nEND_RUNG"), null));

        Assert.Equal(original.Networks[0].Pragma!.Lines, edited.Networks[0].Pragma!.Lines);
        Assert.Equal("RUNG #Start\n    Coil( #Enable )\nEND_RUNG", edited.Networks[0].Code);
    }

    [Fact]
    public void Replace_NetworkWithoutPragma_AddsOneOnlyForALanguage()
    {
        var bare = SimaticSdDocument.Parse("FUNCTION \"F\" : Void\n    NETWORK\n        #a := 1;\n    END_NETWORK\nEND_FUNCTION\n");
        Assert.Null(SimaticSdDocument.Parse(bare.ReplaceNetwork(1, SimaticSdText.NormalizeNetworkCode("#a := 2;"), null)).Networks[0].Pragma);
        var typed = SimaticSdDocument.Parse(bare.ReplaceNetwork(1, SimaticSdText.NormalizeNetworkCode("#a := 2;"), "SCL")).Networks[0];
        Assert.Equal("    { S7_Language := \"SCL\" }", typed.Pragma!.Lines.Single());
    }

    [Fact]
    public void Delete_RemovesTheNetworkWithItsPragmaAndKeepsTheRest()
    {
        var original = Doc(SimaticSdFixtures.CookbookMixed);
        var edited = SimaticSdDocument.Parse(original.DeleteNetwork(3));

        Assert.Equal(new[] { "LAD", "LAD", "LAD" }, edited.Networks.Select(n => n.Language));
        Assert.DoesNotContain("#Sum := #Va + #Vb;", edited.Render());
        Assert.Contains("// NET3 SCL", edited.Render());
        Assert.Equal("target_not_found", Assert.Throws<SimaticSdException>(() => original.DeleteNetwork(5)).Category);
        Assert.Contains("network 5 does not exist: the block has 4 network(s)", Assert.Throws<SimaticSdException>(() => original.Network(5)).Message);
    }

    [Fact]
    public void Pragma_RewriteKeepsUnknownKeysAndTheLayout()
    {
        var lines = new[] { "    {", "        S7_Language := \"LAD\";", "        S7_Custom := \"keep; me\";", "        S7_NetworkTitle := \"MLC_1\"", "    }" };
        Assert.True(SimaticSdPragma.TryParse(lines, out var pragma));
        Assert.Equal("keep; me", pragma.Get("S7_Custom"));

        var changed = pragma.Rewrite(new[] { new SimaticSdPragmaEntry("S7_Language", "\"FBD\"") });
        Assert.Equal(lines.Select(l => l.Replace("\"LAD\"", "\"FBD\"")), changed);
        var added = pragma.Rewrite(new[] { new SimaticSdPragmaEntry("S7_New", "\"1\"") });
        Assert.True(SimaticSdPragma.TryParse(added.ToList(), out var reparsed));
        Assert.Equal(new[] { "S7_Language", "S7_Custom", "S7_NetworkTitle", "S7_New" }, reparsed.Entries.Select(e => e.Key));
        Assert.Equal("        S7_New := \"1\"", added[^2]);
        Assert.Same(pragma.Lines, pragma.Rewrite(Array.Empty<SimaticSdPragmaEntry>()));
    }

    // ------------------------------------------------------------------ .s7res, export forms, files

    [Fact]
    public void Resources_ResolveTitlesPerCulture()
    {
        var texts = SimaticSdResources.Parse(SimaticSdFixtures.CookbookFbLadV3.S7res);
        Assert.Equal("N1", texts["MLC_463"].Single(t => t.Culture == "zh-CN").Text);
        var placeholder = SimaticSdResources.Parse(SimaticSdFixtures.CookbookMixed.S7res)["MLC_placeholder"];
        Assert.Equal(new[] { ("zh-CN", "x"), ("en-US", "x") }, placeholder.Select(t => (t.Culture, t.Text)));
        var yaml = SimaticSdResources.Parse("MultiLingualTexts:\n  - id: 'MLC_a'\n    en-US: 'It''s'\n    de-DE: |\n      Zeile 1\n      Zeile 2\n");
        Assert.Equal(new[] { ("en-US", "It's"), ("de-DE", "Zeile 1\nZeile 2") }, yaml["MLC_a"].Select(t => (t.Culture, t.Text)));
    }

    [Fact]
    public void ExportForms_FindTheReportedExportOnlyFormsOnly()
    {
        var document = Doc(SimaticSdFixtures.SyntheticExportForms);
        Assert.Equal(new[] { "Gt{" }, SimaticSdExportForms.Find(document.Networks[0].Code));
        Assert.Equal(new[] { "Move{", "PBox(" }, SimaticSdExportForms.Find(document.Networks[1].Code).OrderBy(f => f, StringComparer.Ordinal));
        Assert.Empty(SimaticSdExportForms.Find(document.Networks[2].Code));
        Assert.All(SimaticSdFixtures.All.Where(f => f != SimaticSdFixtures.SyntheticExportForms),
            f => Assert.All(Doc(f).Networks, n => Assert.Empty(SimaticSdExportForms.Find(n.Code))));
    }

    [Fact]
    public void FileText_KeepsOneBomOnARoundTrip()
    {
        var withBom = SimaticSdFileText.Encode("﻿text");
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'t' }, withBom.Take(4));
        Assert.Equal(withBom, SimaticSdFileText.Encode("text"));
        Assert.Equal("﻿text", SimaticSdFileText.Decode(withBom));
        Assert.Equal("text", SimaticSdFileText.Decode(new byte[] { (byte)'t', (byte)'e', (byte)'x', (byte)'t' }));
    }

    [Fact]
    public void BlockDocument_RequiresOneS7dcl_AndAtMostOneS7resOfTheSameName()
    {
        var fixture = SimaticSdFixtures.CookbookFcLad;
        Assert.Equal("MCPVerify_FC_LAD", SimaticSdBlockDocument.TryCheck(fixture.Bundle, out _)!.BlockName);
        Assert.Empty(SimaticSdBlockDocument.TryCheck(fixture.Bundle, out _)!.WithoutBom);
        Assert.Equal(new[] { "FC_Branches.s7dcl", "FC_Branches.s7res" }, SimaticSdBlockDocument.TryCheck(SimaticSdFixtures.BetterTiaFcBranches.Bundle, out _)!.WithoutBom);
        string? Error(string content) { _ = SimaticSdBlockDocument.TryCheck(content, out var error); return error; }
        // E5 (live 2026-11-02): TIA Portal's own export of a block without texts has no .s7res, so none is required.
        var withoutTexts = SimaticSdBlockDocument.TryCheck($"--- FILE: A.s7dcl ---\n{fixture.S7dcl}", out var noResError);
        Assert.Null(noResError);
        Assert.NotNull(withoutTexts);
        Assert.Null(withoutTexts!.S7res);
        Assert.Null(withoutTexts.S7resName);
        Assert.Empty(withoutTexts.Cultures);
        Assert.Null(withoutTexts.CultureNote);
        Assert.Contains("at most one .s7res", Error($"--- FILE: A.s7dcl ---\n{fixture.S7dcl}--- FILE: A.s7res ---\n{fixture.S7res}--- FILE: a.s7res ---\n{fixture.S7res}"));
        Assert.Contains("only the .s7dcl", Error($"--- FILE: A.s7dcl ---\n{fixture.S7dcl}--- FILE: A.xml ---\n<x/>\n"));
        Assert.Contains("same name", Error($"--- FILE: A.s7dcl ---\n{fixture.S7dcl}--- FILE: B.s7res ---\n{fixture.S7res}"));
        Assert.Contains("declares no block", Error("--- FILE: A.s7dcl ---\n{ S7_Version := \"0.1\" }\n--- FILE: A.s7res ---\nMultiLingualTexts:\n"));
        Assert.Contains("must be an sd bundle", Error(fixture.S7dcl));
    }

    /// <summary>E4 (live 2026-11-02): an .s7res culture the project lacks failed the import; the cultures are listed, nothing is refused.</summary>
    [Fact]
    public void BlockDocument_ListsTheS7resCultures_WithTheLiveEvidence()
    {
        var s7dcl = "{ S7_Version := \"0.1\" }\nFUNCTION \"FC_T\" : Void\nEND_FUNCTION\n";
        var bundle = $"--- FILE: FC_T.s7dcl ---\n{s7dcl}--- FILE: FC_T.s7res ---\nMultiLingualTexts:\n  - id: MLC_T1\n    de-DE: Titel\n    en-US: Title\n  - id: MLC_K1\n    de-DE: Kommentar\n";
        var document = SimaticSdBlockDocument.TryCheck(bundle, out var error);
        Assert.Null(error);
        Assert.Equal(new[] { "de-DE", "en-US" }, document!.Cultures);
        Assert.Contains("de-DE, en-US", document.CultureNote);
        Assert.Contains("must be a language of the project", document.CultureNote);
        Assert.Contains("Document option", document.CultureNote);
        Assert.Null(PlcOperationCatalog.DocumentError(bundle));
    }

    // ------------------------------------------------------------------ formats and hashes

    [Fact]
    public void SdFormat_IsABlockFormatWithItsOwnHashTag()
    {
        Assert.Equal(new[] { "source", "xml" }, SourceFormatNames.Allowed);
        Assert.False(SourceFormatNames.TryNormalize("sd", SourceFormatNames.Xml, out _, out _));
        Assert.True(SourceFormatNames.TryNormalizeBlock("SD", SourceFormatNames.Xml, out var normalized, out _));
        Assert.Equal("sd", normalized);
        Assert.Equal("sd", PlcFormatNames.Normalize("get_block_content", "sd"));
        Assert.Equal("sd", PlcFormatNames.Normalize(SimaticSdMethods.InsertNetwork, null));
        Assert.Equal("sd", PlcFormatNames.Normalize(SimaticSdMethods.ListNetworks, null));
        Assert.Throws<ArgumentException>(() => PlcFormatNames.Normalize("get_type_content", "sd"));
        var hash = ContentHashRules.Compute("sd", SimaticSdFixtures.CookbookMixed.Bundle);
        Assert.StartsWith("sd:sha256:", hash);
        Assert.True(ContentHashRules.TryParse(hash, out var format, out _));
        Assert.Equal("sd", format);
        Assert.Equal(hash, TiaMcpServer.Safety.Pipeline.ContentHashes.Compute("sd", SimaticSdFixtures.CookbookMixed.Bundle));
    }

    // ------------------------------------------------------------------ list_networks and verification

    [Fact]
    public void Listing_ResolvesTitlesCommentsAndTheHash()
    {
        var fixture = SimaticSdFixtures.CookbookFbLadV3;
        var listing = PlcSdNetworkReader.Build("PLC_1/X", fixture.Bundle);

        Assert.Equal(4, listing.NetworkCount);
        Assert.Equal(("MLC_463", "N1"), (listing.Networks[0].TitleId!, listing.Networks[0].Title.Single().Text));
        Assert.Equal("N1 TON Static", listing.Networks[0].Comment.Single().Text);
        Assert.Equal(ContentHashRules.Compute("sd", fixture.Bundle), listing.ContentHash);
        Assert.Contains(listing.HeaderPragmas, p => p.Key == "S7_PreferredLanguage" && p.Value == "LAD");
        Assert.Equal("MCPVerify_FB_LAD_v3.s7res", listing.S7resDocument);
        Assert.True(PlcSdNetworkReader.Build("PLC_1/S", SimaticSdFixtures.BetterTiaFbSafety.Bundle).FailSafe);
    }

    [Fact]
    public void NetworkCheck_IsStructuralAndReportsTheCodeComparisonAsInformation()
    {
        var fixture = SimaticSdFixtures.CookbookMixed;
        var code = SimaticSdText.NormalizeNetworkCode("#Sum := #Va + #Vb;").Normalized;
        var match = PlcVerificationPolicy.Check(new PlcSdNetworkExpectation(SimaticSdMethods.ReplaceNetwork, 3, 4, "SCL", code, null), fixture.Bundle);
        Assert.True(match.Success);
        Assert.StartsWith("codeMatchesExport: true", match.CodeNote);

        var normalized = PlcVerificationPolicy.Check(new PlcSdNetworkExpectation(SimaticSdMethods.ReplaceNetwork, 3, 4, "SCL", "#Sum := #Va - #Vb;", null), fixture.Bundle);
        Assert.True(normalized.Success);
        Assert.StartsWith("codeMatchesExport: false", normalized.CodeNote);
        Assert.Contains("#Sum := #Va - #Vb;", normalized.Expected);
        Assert.Contains("#Sum := #Va + #Vb;", normalized.Actual);

        Assert.Contains("4 network(s) instead of 5", PlcVerificationPolicy.Check(new PlcSdNetworkExpectation(SimaticSdMethods.InsertNetwork, 3, 5, "SCL", code, null), fixture.Bundle).Problem);
        Assert.Contains("is SCL, not LAD", PlcVerificationPolicy.Check(new PlcSdNetworkExpectation(SimaticSdMethods.InsertNetwork, 3, 4, "LAD", code, null), fixture.Bundle).Problem);
        var deleted = PlcVerificationPolicy.Check(new PlcSdNetworkExpectation(SimaticSdMethods.DeleteNetwork, 2, 4, null, null,
            SimaticSdDocument.Parse(fixture.S7dcl).Networks.Select(n => SimaticSdText.NormalizeForComparison(n.BodyLines)).ToList()), fixture.Bundle);
        Assert.True(deleted.Success);
        Assert.StartsWith("remainingNetworksMatchExport: true", deleted.CodeNote);
        Assert.False(PlcVerificationPolicy.Check(new PlcSdNetworkExpectation(SimaticSdMethods.DeleteNetwork, 2, 3, null, null, null), fixture.Bundle).Success);
    }

    // ------------------------------------------------------------------ catalog, policy, descriptions

    private static string? WriteError(PlcOperationRequest item)
    {
        var validation = PlcOperationCatalog.ValidateWrite(new[] { item });
        return validation.IsValid ? null : validation.Error;
    }

    [Fact]
    public void Catalog_ValidatesTheSdFieldsStructurallyOnly()
    {
        PlcOperationRequest Op(string operation, Action<PlcOperationRequest> configure)
        {
            var op = new PlcOperationRequest { OperationId = "a", Operation = operation, BlockPath = "PLC_1/FB" };
            configure(op);
            return op;
        }

        Assert.Null(WriteError(Op(SimaticSdMethods.InsertNetwork, o => o.Code = "RUNG wire#powerrail\nEND_RUNG")));
        Assert.Null(WriteError(Op(SimaticSdMethods.InsertNetwork, o => { o.Code = "#a := 1;"; o.Language = "STL_Whatever"; o.Position = 2; })));
        Assert.Contains("networkNumber is required", WriteError(Op(SimaticSdMethods.ReplaceNetwork, o => o.Code = "#a := 1;")));
        Assert.Contains("networkNumber is required", WriteError(Op(SimaticSdMethods.DeleteNetwork, _ => { })));
        Assert.Contains("language must be a language name", WriteError(Op(SimaticSdMethods.InsertNetwork, o => { o.Code = "#a := 1;"; o.Language = "LAD\""; })));
        Assert.Contains("contradicts", WriteError(Op(SimaticSdMethods.InsertNetwork, o => { o.Code = "{ S7_Language := \"FBD\" }\n#a := 1;"; o.Language = "LAD"; })));
        Assert.Contains("cannot be detected", WriteError(Op(SimaticSdMethods.InsertNetwork, o => o.Code = "RUNG #Start\nEND_RUNG")));
        Assert.Contains("exactly one network", WriteError(Op(SimaticSdMethods.InsertNetwork, o => o.Code = "NETWORK\n#a := 1;\nEND_NETWORK\nNETWORK\n#b := 1;\nEND_NETWORK")));
        Assert.Contains("'code' is not valid for delete_network", WriteError(Op(SimaticSdMethods.DeleteNetwork, o => { o.NetworkNumber = 1; o.Code = "#a := 1;"; })));
        Assert.Contains("'position' is not valid for replace_network", WriteError(Op(SimaticSdMethods.ReplaceNetwork, o => { o.NetworkNumber = 1; o.Code = "#a := 1;"; o.Position = 1; })));
        Assert.Contains("taken from the 'xml' format but the write uses the 'sd' format", WriteError(Op(SimaticSdMethods.DeleteNetwork, o =>
        {
            o.NetworkNumber = 1;
            o.ExpectedContentHash = ContentHashRules.Compute("xml", "x");
        })));
        Assert.Null(WriteError(Op(SimaticSdMethods.DeleteNetwork, o => { o.NetworkNumber = 1; o.ExpectedContentHash = ContentHashRules.Compute("sd", "x"); })));
        Assert.Null(WriteError(new PlcOperationRequest { OperationId = "c", Operation = SimaticSdMethods.CreateBlockFromDocument, TargetGroupPath = "PLC_1/Blocks", Content = SimaticSdFixtures.CookbookFcLad.Bundle }));
        // The .s7res is optional (E5): a bundle with only the .s7dcl is valid.
        Assert.Null(WriteError(new PlcOperationRequest { OperationId = "c", Operation = SimaticSdMethods.CreateBlockFromDocument, TargetGroupPath = "PLC_1/Blocks",
            Content = $"--- FILE: A.s7dcl ---\n{SimaticSdFixtures.CookbookFcLad.S7dcl}" }));
        Assert.Contains("deterministic block group path", WriteError(new PlcOperationRequest { OperationId = "c", Operation = SimaticSdMethods.CreateBlockFromDocument, TargetGroupPath = "PLC_1",
            Content = SimaticSdFixtures.CookbookFcLad.Bundle }));
        Assert.True(PlcOperationCatalog.ValidateRead(new[] { new PlcOperationRequest { OperationId = "l", Operation = SimaticSdMethods.ListNetworks, BlockPath = "PLC_1/FB" } }).IsValid);
        Assert.True(PlcOperationCatalog.ValidateWrite(new[] { new PlcOperationRequest { OperationId = "u", Operation = "update_block_logic", BlockPath = "PLC_1/FB", Format = "sd", Content = "x",
            ExpectedContentHash = ContentHashRules.Compute("sd", "x") } }).IsValid);
    }

    [Fact]
    public void Policy_MapsTheSdOperationsToPlcReadAndPlcWrite()
    {
        Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, SimaticSdMethods.ListNetworks));
        Assert.Equal("plc_read", ProjectCapabilityCatalog.OperationFor(SimaticSdMethods.ListNetworks));
        foreach (var write in SimaticSdMethods.NetworkEdits.Append(SimaticSdMethods.CreateBlockFromDocument))
        {
            Assert.False(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, write));
            Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadWrite, write));
            Assert.Equal("plc_write", ProjectCapabilityCatalog.OperationFor(write));
        }

        Assert.True(PlcBlockEditMethods.IsWorkerMethod(SimaticSdMethods.CreateBlockFromDocument));
        Assert.All(SimaticSdMethods.NetworkEdits, edit => Assert.False(PlcBlockEditMethods.IsWorkerMethod(edit)));
        Assert.Contains(SimaticSdMethods.ListNetworks, PlcOperationCatalog.ReadOperationNames);
    }

    [Fact]
    public void Descriptions_NameEverySdOperationAndTheNewFields()
    {
        static string Describe(MemberInfo member) => member.GetCustomAttribute<DescriptionAttribute>()!.Description;
        var operation = Describe(typeof(PlcOperationRequest).GetProperty(nameof(PlcOperationRequest.Operation))!);
        // K91: the per-operation text moved from the tool descriptions to the describe_operations reference.
        var read = PlcOperationReference.ReadTool;
        var write = PlcOperationReference.WriteTool;
        Assert.Contains(SimaticSdMethods.ListNetworks, operation);
        Assert.Contains("list_networks (blockPath", read);
        Assert.Contains("format xml|source|sd", read);
        foreach (var name in SimaticSdMethods.NetworkEdits.Append(SimaticSdMethods.CreateBlockFromDocument))
        {
            Assert.Contains(name, operation);
            Assert.Contains(name + " (", write);
        }

        // Every catalog operation is named in the operation field's description.
        Assert.All(PlcOperationCatalog.ReadOperationNames.Concat(PlcOperationCatalog.WriteOperationNames), name => Assert.Contains(name, operation));
        Assert.Contains("insert_network", Describe(typeof(PlcOperationRequest).GetProperty(nameof(PlcOperationRequest.Code))!));
        Assert.Contains("one past the last appends", Describe(typeof(PlcOperationRequest).GetProperty(nameof(PlcOperationRequest.Position))!));
        Assert.Contains("sd", Describe(typeof(PlcOperationRequest).GetProperty(nameof(PlcOperationRequest.Format))!));
        Assert.Contains("create_block_from_document", Describe(typeof(PlcOperationRequest).GetProperty(nameof(PlcOperationRequest.TargetGroupPath))!));
        Assert.Contains("networkCode", write);
        Assert.Contains("codeMatchesExport", write);
    }
}
