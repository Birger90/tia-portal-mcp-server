using System.Text;
using System.Text.RegularExpressions;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc.Interface;
using TiaMcpServer.Contracts.Plc.SimaticSd;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Contracts.Plc.Safety;

/// <summary>What the main safety block route did (<see cref="PlcMainSafetyBlockSdRoute"/>), on success and on failure.</summary>
public sealed class PlcMainSafetyBlockRouteInfo
{
    /// <summary>Always <see cref="PlcMainSafetyBlockSdRoute.Route"/>.</summary>
    public string Route { get; set; } = PlcMainSafetyBlockSdRoute.Route;

    /// <summary>The operation whose SD document took the route (update_block_logic, or create_block_from_document).</summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>The main safety block as addressed.</summary>
    public string BlockPath { get; set; } = string.Empty;

    public string BlockName { get; set; } = string.Empty;

    /// <summary>The F-runtime groups whose MainSafetyBlockName named the block before the write.</summary>
    public List<string> RuntimeGroups { get; set; } = new();

    /// <summary>The temporary F-block (<c>zzMcpTmp_&lt;block&gt;_&lt;8 hex&gt;</c>).</summary>
    public string TemporaryBlockName { get; set; } = string.Empty;

    /// <summary>The group the temporary block is imported into (the main safety block's group).</summary>
    public string TemporaryBlockPath { get; set; } = string.Empty;

    /// <summary><see cref="PlcMainSafetyBlockSdRoute.TemporaryStates"/>: notCreated, deleted or left (cleanup failed: the name above).</summary>
    public string TemporaryBlockState { get; set; } = PlcMainSafetyBlockSdRoute.TemporaryStates.NotCreated;

    /// <summary>Why the temporary block could not be deleted; null otherwise.</summary>
    public string? CleanupProblem { get; set; }

    /// <summary>The compile of the temporary block; null when it was not compiled.</summary>
    public PlcMainSafetyBlockRouteCompileInfo? TemporaryCompile { get; set; }

    /// <summary><see cref="PlcMainSafetyBlockSdRoute.MainImports"/>: notStarted (the main safety block is unchanged), simaticMl (imported as SimaticML) or failed.</summary>
    public string MainImport { get; set; } = PlcMainSafetyBlockSdRoute.MainImports.NotStarted;

    /// <summary>The number set in the transplanted SimaticML (the main block's); null before that step.</summary>
    public int? MainNumber { get; set; }

    /// <summary>The AutoNumber set in the transplanted SimaticML (the main block's); null when unknown or before that step.</summary>
    public bool? MainAutoNumber { get; set; }

    /// <summary>Where the main block's number and AutoNumber were read: simaticMlExport, or liveAttributes when the main block exported no SimaticML.</summary>
    public string? IdentitySource { get; set; }

    /// <summary>Every step taken, in order.</summary>
    public List<string> Steps { get; set; } = new();

    /// <summary>Changes made to the temporary document (name, removed S7_BlockNumber, added S7_Safety).</summary>
    public List<string> Notes { get; set; } = new();
}

/// <summary>The compile of the temporary F-block (the existing compile path for one block).</summary>
public sealed class PlcMainSafetyBlockRouteCompileInfo
{
    public string State { get; set; } = string.Empty;

    public int ErrorCount { get; set; }

    public int WarningCount { get; set; }

    /// <summary>The compiler messages as "severity path: description", at most <see cref="PlcMainSafetyBlockSdRoute.MaxCompileMessages"/>.</summary>
    public List<string> Messages { get; set; } = new();
}

/// <summary>The main safety block's identity for the transplant.</summary>
public sealed record PlcMainSafetyBlockIdentity(int Number, bool? AutoNumber, string Source);

/// <summary>The Siemens steps of the route; the worker implements them on Openness, the FakeWorker on its model.</summary>
public interface IPlcMainSafetyBlockSdRouteSteps<TImport>
{
    /// <summary>True when a block of that name exists anywhere in the PLC.</summary>
    bool TemporaryExists(string name);

    /// <summary>ImportFromDocuments(..., Override) of the temporary documents into the main safety block's group.</summary>
    void ImportTemporary(string name, string s7dcl, string? s7res);

    /// <summary>compile_check of the one temporary block.</summary>
    CompileCheckReport CompileTemporary(string name);

    /// <summary>The temporary block's SimaticML; null with the reason when TIA Portal exported none.</summary>
    string? ExportTemporarySimaticMl(string name, out string? reason);

    /// <summary>The main safety block's current number and AutoNumber.</summary>
    PlcMainSafetyBlockIdentity MainIdentity();

    /// <summary>update_block_logic format xml of the transplanted SimaticML onto the main safety block (with its current xml hash).</summary>
    TImport ImportMain(string simaticMl);

    /// <summary>Deletes the temporary block.</summary>
    void DeleteTemporary(string name);

    /// <summary>A lost TIA Portal: escapes every step, no cleanup is attempted.</summary>
    bool IsFatal(Exception exception);

    /// <summary>The worker failure category of a step's exception.</summary>
    string CategoryOf(Exception exception);
}

/// <summary>A failed route: the category, the message and what was done (the main safety block's state is in <see cref="Info"/>).</summary>
public sealed class PlcMainSafetyBlockRouteException(string category, string message, PlcMainSafetyBlockRouteInfo info, Exception? inner = null)
    : Exception(message, inner)
{
    public string Category { get; } = category;

    public PlcMainSafetyBlockRouteInfo Info { get; } = info;
}

/// <summary>
/// The route of an SD-document write onto the main safety block of an F-runtime group (live workaround, TIA Portal V21,
/// 2026-11-02, project MCP_Safety_01). L1: every SIMATIC SD import (<c>ImportFromDocuments</c> Override) of a main safety
/// block made TIA Portal drop the runtime group's MainSafetyBlockName, and the PLC compile then failed ("does not have a valid
/// main safety block"); L2: the group could not be re-linked (no public property MainSafetyBlock, no attribute of that name in
/// GetAttributeInfos()); L3: a SimaticML import of the same block kept the reference. L4, done live by hand with 0 compile
/// errors afterwards and the reference intact: the SD document is imported as a temporary F-block into the main block's
/// group, that block is compiled (TIA Portal exports no SimaticML of an uncompiled, inconsistent block: BetterTIA pitfall B4,
/// confirmed live), its SimaticML is exported, its Name, Number and AutoNumber are set to the main block's
/// (<see cref="SimaticMlBlockIdentity"/>) and the document is imported onto the main block with update_block_logic format xml;
/// then the temporary block is deleted (deleting it before the main import terminated TIA Portal V21 live, K36). <see cref="Run{TImport}"/> runs these steps in one worker call, stops before the main
/// block is touched when the temporary block does not compile or export, and deletes the temporary block on every path
/// (a block it cannot delete is reported by name, never left silently). Siemens-free; shared by the worker and the FakeWorker.
/// </summary>
public static class PlcMainSafetyBlockSdRoute
{
    public const string Route = "sdViaXmlForMainSafetyBlock";

    public const string TemporaryPrefix = "zzMcpTmp_";

    /// <summary>TIA Portal's maximum block name length (characters); the block part of the temporary name is shortened to fit.</summary>
    public const int MaxBlockNameLength = 125;

    public const int MaxCompileMessages = 20;

    private const int MaxCompileMessageChars = 300;

    public static class TemporaryStates
    {
        public const string NotCreated = "notCreated";
        public const string Deleted = "deleted";
        public const string Left = "left";
    }

    public static class MainImports
    {
        public const string NotStarted = "notStarted";
        public const string SimaticMl = "simaticMl";
        public const string Failed = "failed";
    }

    public static class IdentitySources
    {
        public const string SimaticMlExport = "simaticMlExport";
        public const string LiveAttributes = "liveAttributes";
    }

    private static readonly Regex DeclarationName = new(
        @"^(?<head>\s*(?:FUNCTION_BLOCK|FUNCTION|ORGANIZATION_BLOCK|DATA_BLOCK)\s+)(?<name>""(?:[^""]|"""")+""|[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.CultureInvariant);

    private static readonly Regex Unsafe = new("[^A-Za-z0-9_]", RegexOptions.CultureInvariant);

    /// <summary>
    /// The deterministic temporary block name: <c>zzMcpTmp_&lt;block&gt;_&lt;8 hex&gt;</c>, the hex digits the start of the
    /// SHA-256 of the block name and the documents, the block part reduced to letters, digits and underscores and shortened so
    /// the name fits <see cref="MaxBlockNameLength"/>. The same write gets the same name, so a leftover of an earlier attempt
    /// is found (and refused) instead of silently doubled.
    /// </summary>
    public static string TemporaryName(string blockName, string s7dcl, string? s7res)
    {
        var hex = ContentHashRules.Sha256Hex(blockName + "\n" + s7dcl + "\n" + (s7res ?? string.Empty)).Substring(0, 8);
        var part = Unsafe.Replace(blockName, "_");
        var room = MaxBlockNameLength - TemporaryPrefix.Length - 1 - hex.Length;
        if (part.Length > room) part = part.Substring(0, room);
        return $"{TemporaryPrefix}{part}_{hex}";
    }

    /// <summary>
    /// The .s7dcl of the temporary F-block: the caller's document with the declared name set to <paramref name="temporaryName"/>,
    /// a header <c>S7_BlockNumber</c> removed (the temporary block must not claim the main block's number; TIA Portal numbers it,
    /// live: auto number 2) and <c>S7_Safety := "TRUE"</c> added when the header has no S7_Safety key (the temporary block must
    /// be an F-block like the main safety block; a present key is kept as written). Everything else stays byte for byte.
    /// </summary>
    /// <exception cref="SimaticSdException">The document declares no block (validation_error).</exception>
    public static (string S7dcl, List<string> Notes) TemporaryDocument(string s7dcl, string temporaryName)
    {
        var document = SimaticSdDocument.Parse(s7dcl);
        if (document.DeclarationLine < 0 || document.BlockName is null)
            throw new SimaticSdException(WorkerFailureCategories.ValidationError, "The .s7dcl document declares no block.");
        var notes = new List<string>();
        var lines = document.Lines.ToList();
        var match = DeclarationName.Match(lines[document.DeclarationLine]);
        if (!match.Success) throw new SimaticSdException(WorkerFailureCategories.ValidationError, "The .s7dcl block declaration could not be read.");
        lines[document.DeclarationLine] = match.Groups["head"].Value + SimaticSdPragma.Quote(temporaryName) + lines[document.DeclarationLine].Substring(match.Length);
        notes.Add($"The temporary document declares '{temporaryName}' instead of '{document.BlockName}'.");

        var header = document.HeaderPragma;
        var updates = new List<SimaticSdPragmaEntry>();
        var removals = new List<string>();
        if (header?.Has("S7_BlockNumber") == true)
        {
            removals.Add("S7_BlockNumber");
            notes.Add($"S7_BlockNumber \"{header.Get("S7_BlockNumber")}\" was removed from the temporary document (TIA Portal numbers the temporary block; the main safety block keeps its number).");
        }

        if (header?.Has("S7_Safety") != true)
        {
            updates.Add(new SimaticSdPragmaEntry("S7_Safety", SimaticSdPragma.Quote("TRUE")));
            notes.Add("S7_Safety := \"TRUE\" was added to the temporary document's header (the main safety block is an F-block).");
        }

        if (header is not null)
        {
            if (updates.Count > 0 || removals.Count > 0)
            {
                var rewritten = header.Rewrite(updates, removals);
                lines.RemoveRange(header.StartLine, header.EndLine - header.StartLine + 1);
                lines.InsertRange(header.StartLine, rewritten);
            }
        }
        else
        {
            lines.Insert(document.DeclarationLine, "{ S7_Safety := \"TRUE\" }");
        }

        return (document.Render(lines), notes);
    }

    /// <summary>
    /// Runs the route: temporary import, compile, SimaticML export, transplant, SimaticML import onto the main safety block,
    /// temporary delete (in this order: deleting first terminated TIA Portal live, K36). Returns the main import's result, the route evidence and the warnings (a temporary block that could not
    /// be deleted). Throws <see cref="PlcMainSafetyBlockRouteException"/> on a non-fatal failure, after the cleanup; a fatal
    /// exception (<see cref="IPlcMainSafetyBlockSdRouteSteps{TImport}.IsFatal"/>) escapes unchanged.
    /// </summary>
    public static (TImport Result, PlcMainSafetyBlockRouteInfo Info, List<string> Warnings) Run<TImport>(
        IPlcMainSafetyBlockSdRouteSteps<TImport> steps, string operation, string blockPath, string blockName, string groupPath,
        IEnumerable<string> runtimeGroups, string s7dcl, string? s7res)
    {
        var temporaryName = TemporaryName(blockName, s7dcl, s7res);
        var info = new PlcMainSafetyBlockRouteInfo
        {
            Operation = operation, BlockPath = blockPath, BlockName = blockName, RuntimeGroups = runtimeGroups.ToList(),
            TemporaryBlockName = temporaryName, TemporaryBlockPath = groupPath.TrimEnd('/') + "/" + temporaryName,
        };
        var warnings = new List<string>();
        string temporaryS7dcl;
        try
        {
            (temporaryS7dcl, info.Notes) = TemporaryDocument(s7dcl, temporaryName);
        }
        catch (SimaticSdException exception)
        {
            throw new PlcMainSafetyBlockRouteException(exception.Category, $"{Lead(info)}: {exception.Message} Nothing was changed.", info, exception);
        }

        if (Step(steps, info, "check that no block named the temporary name exists", () => steps.TemporaryExists(temporaryName)))
            throw new PlcMainSafetyBlockRouteException(WorkerFailureCategories.ValidationError,
                $"{Lead(info)}: a block named '{temporaryName}' already exists in the PLC (a leftover of an earlier attempt with the same document?). "
                + "It is not overwritten: inspect it, delete it with plc_write delete_block if it is a leftover, then retry. Nothing was changed.", info);

        var mainTouched = false;
        try
        {
            try
            {
                steps.ImportTemporary(temporaryName, temporaryS7dcl, s7res);
                info.TemporaryBlockState = TemporaryStates.Left;
                info.Steps.Add($"imported the SD document as the temporary F-block '{info.TemporaryBlockPath}' (ImportFromDocuments Override)");
            }
            catch (Exception exception) when (!steps.IsFatal(exception))
            {
                // A failed import may still have created the block.
                if (Exists(steps, temporaryName)) info.TemporaryBlockState = TemporaryStates.Left;
                throw Failure(steps, info, exception, $"the SD document could not be imported as the temporary F-block '{temporaryName}': {exception.Message}");
            }

            CompileCheckReport report;
            try
            {
                report = steps.CompileTemporary(temporaryName);
            }
            catch (Exception exception) when (!steps.IsFatal(exception))
            {
                throw Failure(steps, info, exception, $"the temporary F-block '{temporaryName}' could not be compiled: {exception.Message}");
            }

            info.TemporaryCompile = Summarize(report);
            info.Steps.Add($"compiled the temporary F-block ({info.TemporaryCompile.State}, {info.TemporaryCompile.ErrorCount} error(s), {info.TemporaryCompile.WarningCount} warning(s))");
            if (info.TemporaryCompile.ErrorCount > 0 || string.Equals(info.TemporaryCompile.State, "Error", StringComparison.OrdinalIgnoreCase))
                throw Failure(steps, info, null,
                    $"the temporary F-block '{temporaryName}' (the SD document) did not compile: {string.Join(" | ", info.TemporaryCompile.Messages.DefaultIfEmpty("no message"))}",
                    WorkerFailureCategories.WorkerOperationFailed);

            string? simaticMl;
            string? reason;
            try
            {
                simaticMl = steps.ExportTemporarySimaticMl(temporaryName, out reason);
            }
            catch (Exception exception) when (!steps.IsFatal(exception))
            {
                simaticMl = null;
                reason = exception.Message;
            }

            if (string.IsNullOrEmpty(simaticMl))
                throw Failure(steps, info, null,
                    $"TIA Portal exported no SimaticML of the compiled temporary F-block '{temporaryName}' ({reason ?? "no reason given"})",
                    WorkerFailureCategories.WorkerOperationFailed);
            info.Steps.Add("exported the temporary F-block as SimaticML");

            PlcMainSafetyBlockIdentity identity;
            string transplanted;
            try
            {
                identity = steps.MainIdentity();
                info.MainNumber = identity.Number;
                info.MainAutoNumber = identity.AutoNumber;
                info.IdentitySource = identity.Source;
                transplanted = SimaticMlBlockIdentity.Apply(simaticMl!, blockName, identity.Number, identity.AutoNumber);
                var check = SimaticMlBlockIdentity.Read(transplanted);
                if (check.Name != blockName || check.Number != identity.Number.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    throw new FormatException("the transplanted document does not read back the main block's name and number");
            }
            catch (Exception exception) when (!steps.IsFatal(exception))
            {
                throw Failure(steps, info, exception, $"the temporary F-block's SimaticML could not be given the main safety block's name and number: {exception.Message}",
                    exception is FormatException ? WorkerFailureCategories.WorkerOperationFailed : null);
            }

            info.Steps.Add($"set Name '{blockName}', Number {identity.Number}"
                + (identity.AutoNumber is { } auto ? $" and AutoNumber {(auto ? "true" : "false")}" : string.Empty)
                + $" in the temporary SimaticML ({identity.Source})");

            // The temporary block stays until the main import has finished. K36 (live 2026-11-02): deleting it before the main
            // import made TIA Portal V21 show "Übersetzen (0125:000006) Das ausgewählte Objekt kann nicht übersetzt werden." during
            // the main import and terminate; with this order the route ran live without a fault (only a compile warning about the
            // temporary block).
            TImport result;
            try
            {
                mainTouched = true;
                result = steps.ImportMain(transplanted);
            }
            catch (Exception exception) when (!steps.IsFatal(exception))
            {
                info.MainImport = MainImports.Failed;
                throw Failure(steps, info, exception, $"the SimaticML import onto the main safety block '{blockPath}' failed: {exception.Message}");
            }

            info.MainImport = MainImports.SimaticMl;
            info.Steps.Add($"imported the SimaticML onto the main safety block '{blockPath}' (update_block_logic format xml with its current xml hash)");
            Cleanup(steps, info, warnings);
            return (result, info, warnings);
        }
        catch (PlcMainSafetyBlockRouteException)
        {
            throw;
        }
        catch (Exception exception) when (!steps.IsFatal(exception))
        {
            if (mainTouched && info.MainImport == MainImports.NotStarted) info.MainImport = MainImports.Failed;
            throw Failure(steps, info, exception, exception.Message);
        }
    }

    private static string Lead(PlcMainSafetyBlockRouteInfo info)
        => $"{info.Operation} of the main safety block '{info.BlockPath}' (F-runtime group(s) {string.Join(", ", info.RuntimeGroups.Select(g => $"'{g}'"))}) through a temporary F-block";

    private static T Step<T, TImport>(IPlcMainSafetyBlockSdRouteSteps<TImport> steps, PlcMainSafetyBlockRouteInfo info, string what, Func<T> call)
    {
        try
        {
            return call();
        }
        catch (Exception exception) when (!steps.IsFatal(exception))
        {
            throw new PlcMainSafetyBlockRouteException(steps.CategoryOf(exception), $"{Lead(info)}: could not {what}: {exception.Message} Nothing was changed.", info, exception);
        }
    }

    private static bool Exists<TImport>(IPlcMainSafetyBlockSdRouteSteps<TImport> steps, string name)
    {
        try
        {
            return steps.TemporaryExists(name);
        }
        catch (Exception exception) when (!steps.IsFatal(exception))
        {
            // Unknown: the cleanup tries anyway.
            return true;
        }
    }

    /// <summary>Cleans up, then builds the failure (the main block's state and the temporary block's fate in the message).</summary>
    private static PlcMainSafetyBlockRouteException Failure<TImport>(IPlcMainSafetyBlockSdRouteSteps<TImport> steps, PlcMainSafetyBlockRouteInfo info, Exception? cause,
        string what, string? category = null)
    {
        var warnings = new List<string>();
        Cleanup(steps, info, warnings);
        var main = info.MainImport switch
        {
            MainImports.NotStarted => $"The main safety block '{info.BlockPath}' was not changed.",
            MainImports.Failed => $"The main safety block '{info.BlockPath}' may have been changed by the failed SimaticML import: inspect it with plc_read get_block_content and compile_check.",
            _ => string.Empty,
        };
        var temporary = info.TemporaryBlockState switch
        {
            TemporaryStates.Deleted => $"The temporary block '{info.TemporaryBlockName}' was deleted.",
            TemporaryStates.Left => $"The temporary block '{info.TemporaryBlockPath}' could NOT be deleted ({info.CleanupProblem}): delete it with plc_write delete_block.",
            _ => string.Empty,
        };
        return new PlcMainSafetyBlockRouteException(category ?? (cause is null ? WorkerFailureCategories.WorkerOperationFailed : steps.CategoryOf(cause)),
            $"{Lead(info)}: {what.TrimEnd('.')}. {main} {temporary}".TrimEnd(), info, cause);
    }

    /// <summary>Deletes a created temporary block; a failure leaves its name in the evidence and a warning.</summary>
    private static void Cleanup<TImport>(IPlcMainSafetyBlockSdRouteSteps<TImport> steps, PlcMainSafetyBlockRouteInfo info, List<string> warnings)
    {
        if (info.TemporaryBlockState != TemporaryStates.Left) return;
        try
        {
            steps.DeleteTemporary(info.TemporaryBlockName);
            if (steps.TemporaryExists(info.TemporaryBlockName))
                throw new InvalidOperationException("the block still exists after Delete()");
            info.TemporaryBlockState = TemporaryStates.Deleted;
            info.Steps.Add($"deleted the temporary F-block '{info.TemporaryBlockName}'");
        }
        catch (Exception exception) when (!steps.IsFatal(exception))
        {
            info.CleanupProblem = exception.Message;
            info.Steps.Add($"could NOT delete the temporary F-block '{info.TemporaryBlockName}': {exception.Message}");
            warnings.Add($"The temporary F-block '{info.TemporaryBlockPath}' of the main safety block route could not be deleted ({exception.Message}). "
                + "Delete it with plc_write delete_block, then compile_check.");
        }
    }

    /// <summary>The compile report reduced to state, counts and bounded messages.</summary>
    public static PlcMainSafetyBlockRouteCompileInfo Summarize(CompileCheckReport report)
    {
        var messages = report.Plcs.SelectMany(p => p.Messages)
            .Where(m => !string.IsNullOrWhiteSpace(m.Description))
            .Select(m => Cut($"{m.Severity} {m.Path}: {m.Description}".Trim()))
            .Take(MaxCompileMessages).ToList();
        return new PlcMainSafetyBlockRouteCompileInfo
        {
            State = report.OverallState, ErrorCount = report.TotalErrorCount, WarningCount = report.TotalWarningCount, Messages = messages,
        };
    }

    private static string Cut(string text)
    {
        var line = text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
        return line.Length <= MaxCompileMessageChars ? line : line.Substring(0, MaxCompileMessageChars - 1) + "…";
    }

    /// <summary>The planning note of an SD-route write onto a main safety block (info guard plc_import_may_unlink_main_safety_block).</summary>
    public static string PlanningNote(string operationId, string blockPath, IEnumerable<string> runtimeGroups)
        => $"Operation '{operationId}' writes an SD document onto '{blockPath}', the main safety block of F-runtime group(s) {string.Join(", ", runtimeGroups.Select(g => $"'{g}'"))}. "
           + "A direct SIMATIC SD import (ImportFromDocuments Override) of a main safety block makes TIA Portal drop the runtime group's MainSafetyBlockName "
           + "(live, V21, 2026-11-02; the PLC compile then fails \"does not have a valid main safety block\", and the reference cannot be re-assigned through Openness), "
           + "so the document is not imported onto the block. Instead the worker, in this one item: (1) imports it as a temporary F-block "
           + $"({TemporaryPrefix}<block>_<8 hex>) into the block's group, (2) compiles that block (TIA Portal exports no SimaticML of an uncompiled block), "
           + "(3) exports its SimaticML, (4) sets Name, Number and AutoNumber to the main block's, (5) imports that SimaticML onto the main safety block "
           + "(update_block_logic format xml with its current hash; the SimaticML route keeps the reference live), (6) deletes the temporary block. "
           + "A compile error of the temporary block stops before the main block is touched. The item result names the route (sdViaXmlForMainSafetyBlock) and the temporary block; "
           + "the verification re-reads the F-runtime groups afterwards (check safetyRuntimeGroup).";

    /// <summary>The runtime groups whose main safety block (not its instance DB) is <paramref name="blockName"/>.</summary>
    public static IReadOnlyList<string> MainBlockGroups(IEnumerable<PlcMainSafetyLink> links, string blockName)
        => links.Where(l => PlcMainSafetyLinks.Same(l.MainSafetyBlockName, blockName)).Select(l => l.RuntimeGroup).ToList();

    /// <summary>The documents of an sd bundle for the route: the one .s7dcl and the optional .s7res.</summary>
    public static (string S7dcl, string? S7res)? Documents(string content)
    {
        if (!PlcBlockBundleText.TryGetDocument(content, SimaticSdText.BundleExtension, out _, out var s7dcl)) return null;
        return (s7dcl, PlcBlockBundleText.TryGetDocument(content, SimaticSdText.ResourceExtension, out _, out var s7res) ? s7res : null);
    }
}
