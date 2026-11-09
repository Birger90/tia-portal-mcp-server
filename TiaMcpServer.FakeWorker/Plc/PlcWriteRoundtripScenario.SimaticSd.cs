using System.Text.RegularExpressions;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Interface;
using TiaMcpServer.Contracts.Plc.SimaticSd;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.FakeWorker.Plc;

/// <summary>
/// SIMATIC SD in the <c>plc-write-roundtrip</c> fixture. PLC_1 has a block group <c>SD</c> with the blocks of
/// <see cref="SimaticSdFixtures"/> (FB_Safety is an F_FBD block of the F-CPU PLC_1); get_block_content sd returns their
/// bundles, and for any other block a minimal generated .s7dcl/.s7res pair without networks. update_block_logic sd
/// checks the declared name, the safety rule and the sd hash like the worker and stores the bundle; to model the
/// reported V21 export form (1748477582/tia-portal-openness-mcp, not verified) a stored <c>Move(</c> is exported as
/// <c>Move{ Card:=1; DisableENO:=TRUE }(</c> (that report was not reproduced live on V21 on 2026-11-02 for Move; the model
/// stays to exercise the informative guard). create_block_from_document imports into a group (a block named
/// <c>RejectedByPortal</c> fails as TIA Portal's import would). This models shapes only; it is no evidence of Siemens behavior.
/// </summary>
sealed partial class PlcWriteRoundtripScenario
{
    private const string SdGroup = "SD";
    private static readonly Regex ImportFormMove = new(@"(?<![A-Za-z0-9_#.""])Move\(", RegexOptions.CultureInvariant);

    private void SeedSimaticSdFixture(Plc plc)
    {
        var group = new Group(SdGroup);
        foreach (var fixture in SimaticSdFixtures.All)
        {
            var document = SimaticSdDocument.Parse(fixture.S7dcl);
            var type = BlockType(document.BlockKind);
            var language = document.IsFailSafe ? "F_" + document.PreferredLanguage : document.PreferredLanguage ?? "LAD";
            group.Blocks.Add((fixture.BlockName, type, language, 900 + group.Blocks.Count));
            _content[Key($"{plc.Software}/Blocks/{SdGroup}/{fixture.BlockName}", SourceFormatNames.Sd)] = fixture.Bundle;
        }

        plc.Root.Groups.Add(group);
    }

    /// <summary>The sd export of a block without seeded documents: its declaration and a minimal .s7res, no networks.</summary>
    private static string GeneratedSd(string block, string type, string? language)
    {
        var (open, close) = type switch
        {
            "FB" => ("FUNCTION_BLOCK", "END_FUNCTION_BLOCK"), "FC" => ("FUNCTION", "END_FUNCTION"), "OB" => ("ORGANIZATION_BLOCK", "END_ORGANIZATION_BLOCK"),
            _ => ("DATA_BLOCK", "END_DATA_BLOCK"),
        };
        var preferred = type is "FB" or "FC" or "OB" ? $"    S7_PreferredLanguage := \"{(language ?? "LAD").Replace("F_", string.Empty)}\";\n" : string.Empty;
        var s7dcl = $"﻿{{\n    S7_Optimized := \"TRUE\";\n{preferred}    S7_Version := \"0.1\"\n}}\n{open} \"{block}\"{(type == "FC" ? " : Void" : string.Empty)}\n{close}\n";
        return $"--- FILE: {block}.s7dcl ---\n{s7dcl}--- FILE: {block}.s7res ---\n﻿MultiLingualTexts:\n  - id: MLC_x\n    en-US: x\n";
    }

    private string ExportSd(Plc plc, string block, string path)
    {
        if (_content.TryGetValue(Key(path, SourceFormatNames.Sd), out var stored)) return stored;
        var entry = AllBlocks(plc.Root).First(b => Names.Equals(b.Name, block));
        return GeneratedSd(block, entry.Type, entry.Language);
    }

    /// <summary>update_block_logic sd: the worker's preflight (documents, declared name, safety, hash), then the stored export.</summary>
    private string UpdateBlockSd(WorkerRequest request, Plc plc, string block, string path)
    {
        var documents = PlcBlockBundleText.Documents(request.Content!);
        if (documents.Any(d => d.Key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
            Throw(WorkerFailureCategories.ValidationError, "format sd takes only the SIMATIC SD documents (.s7dcl, .s7res); this bundle has a SimaticML (.xml) document.");
        if (!PlcBlockBundleText.TryGetDocument(request.Content!, ".s7dcl", out _, out var s7dcl))
            Throw(WorkerFailureCategories.ValidationError, "format sd needs exactly one .s7dcl document; the bundle has 0.");
        var declared = SimaticSdDocument.Parse(s7dcl).BlockName;
        if (!string.Equals(declared, block, StringComparison.Ordinal))
            Throw(WorkerFailureCategories.ValidationError, $"The .s7dcl document declares '{declared}' but '{request.BlockPath}' resolves to '{block}'.");
        RequireFailSafeImportPermitted(plc, block, request.Content);
        RequireHash(request, SourceFormatNames.Sd, ExportSd(plc, block, path));
        ApplyDocumentImportOption(request.DocumentImportOption, request.Content);
        // Onto a main safety block the worker never imports the SD document (live 2026-11-02, L1-L4): the temporary F-block route.
        if (MainSafetyGroups(plc, block) is { Count: > 0 } groups)
            return UpdateMainSafetyBlockThroughRoute(request, plc, ResolveBlock(request.BlockPath!).Group, block, path, groups);
        _content[Key(path, SourceFormatNames.Sd)] = ImportFormMove.Replace(request.Content!, "Move{ Card:=1; DisableENO:=TRUE }(");
        _exports[path] = 0;
        _written.Add(path);
        var warnings = DropAndRestoreMainSafetyReferences(plc, block, request.BlockPath!);
        return System.Text.Json.JsonSerializer.Serialize(new WorkerResponse
            {
                Success = true, Payload = "Import succeeded.", BlockImportOutcome = completedOutcome(SourceFormatNames.Sd), Warnings = warnings.Count == 0 ? null : warnings,
            },
            TiaMcpServer.Contracts.Json.WorkerJson.Envelope);
    }

    private PlcBlockEditResultInfo CreateBlockFromDocument(PlcBlockEditRequestInfo edit)
    {
        var document = SimaticSdBlockDocument.TryCheck(edit.Content ?? string.Empty, out var error)
            ?? Throw<SimaticSdBlockDocument>(WorkerFailureCategories.ValidationError, $"create_block_from_document: {error}");
        var address = BlockAddress.Parse(edit.TargetGroupPath!.TrimEnd('/') + "/_");
        var plc = FindPlc(address.PlcName);
        var group = Walk(plc.Root, address.FolderPath);
        var existing = UserBlocks(plc.Root, new List<string>()).Where(m => Names.Equals(m.Block, document.BlockName)).ToList();
        if (existing.Count > 0 && edit.Overwrite != true)
            Throw(WorkerFailureCategories.ValidationError, $"A block named '{document.BlockName}' already exists at '{BlocksPath(plc, existing[0].Folders, existing[0].Block)}'. Set overwrite:true. Nothing was imported.");
        RequireFailSafeImportPermitted(plc, document.BlockName, edit.Content, SimaticSdMethods.CreateBlockFromDocument);
        if (document.BlockName == RejectedTagName)
            Throw(WorkerFailureCategories.WorkerOperationFailed, $"TIA Portal returned SIMATIC SD import state 'Failure' for '{document.BaseName}': Syntax Error: scripted rejection");
        ApplyDocumentImportOption(edit.DocumentImportOption, edit.Content);
        var replacedPath = existing.Count > 0 ? BlocksPath(plc, existing[0].Folders, existing[0].Block) : null;
        var messages = new List<string>();
        if (document.CultureNote is { } cultureNote) messages.Add(cultureNote);
        if (replacedPath is not null && MainSafetyGroups(plc, existing[0].Block) is { Count: > 0 } groups)
            return CreateOntoMainSafetyBlock(plc, existing[0].Group, existing[0].Block, replacedPath, groups, document, edit.Content!, messages);
        foreach (var (owner, _, name) in existing) owner.Blocks.RemoveAll(b => Names.Equals(b.Name, name));
        var type = BlockType(document.Parsed.BlockKind);
        group.Blocks.Add((document.BlockName, type, type == "GlobalDB" ? "DB" : (document.IsFailSafe ? "F_" : string.Empty) + (document.Parsed.PreferredLanguage ?? "LAD"),
            Enumerable.Range(1, 60000).First(n => !AllBlocks(plc.Root).Any(b => b.Type == type && b.Number == n))));
        var path = BlocksPath(plc, address.FolderPath, document.BlockName);
        // Like TIA Portal's export of a block without texts (live 2026-11-02, E1), a document without .s7res stores none.
        _content[Key(path, SourceFormatNames.Sd)] = $"--- FILE: {document.BlockName}.s7dcl ---\n{WithBom(document.S7dcl)}{(document.S7dcl.EndsWith("\n", StringComparison.Ordinal) ? string.Empty : "\n")}"
            + (document.S7res is null ? string.Empty : $"--- FILE: {document.BlockName}.s7res ---\n{WithBom(document.S7res)}");
        // Not compiled yet: TIA Portal exports no SimaticML of it until compile_check (B4, confirmed live 2026-11-02).
        _uncompiled.Add(path);
        if (document.WithoutBom.Count > 0) messages.Add($"{string.Join(" and ", document.WithoutBom)} written with a UTF-8 BOM.");
        if (replacedPath is not null) messages.AddRange(DropAndRestoreMainSafetyReferences(plc, document.BlockName, replacedPath));
        return new PlcBlockEditResultInfo { Operation = SimaticSdMethods.CreateBlockFromDocument, Block = Identity(plc, path), Messages = messages };
    }

    private static string WithBom(string text) => SimaticSdFileText.HasBom(text) ? text : "﻿" + text;

    private static string BlockType(string? kind) => kind switch
    {
        "FUNCTION_BLOCK" => "FB", "FUNCTION" => "FC", "ORGANIZATION_BLOCK" => "OB", _ => "GlobalDB",
    };
}
