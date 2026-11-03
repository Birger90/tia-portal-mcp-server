using System.Text.Json;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Interface;
using TiaMcpServer.Contracts.Plc.Safety;
using TiaMcpServer.Contracts.Plc.SimaticSd;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.FakeWorker.Plc;

/// <summary>
/// The main safety block route in the <c>plc-write-roundtrip</c> fixture, modelled on the live findings of TIA Portal V21
/// (2026-11-02): L1, a SIMATIC SD import of a main safety block drops the runtime group's MainSafetyBlockName
/// (<see cref="DropAndRestoreMainSafetyReferences"/>, used only when <c>noRoute</c> models a worker that could not tell the
/// block is a main safety block); L2, the reference cannot be re-assigned (<c>noRelink</c>); L3, the SimaticML import keeps it;
/// L4, the shared <see cref="PlcMainSafetyBlockSdRoute.Run{TImport}"/> with steps on this model. A block created from an SD
/// document is uncompiled until compile_check (or the route's compile) and exports no SimaticML meanwhile (BetterTIA pitfall B4,
/// confirmed live): get_block_content xml then returns its SD documents with the D15 warning. Markers in the .s7dcl make a
/// step fail: <c>fake:temp-import-fails</c>, <c>fake:compile-error</c>, <c>fake:export-missing</c>, <c>fake:main-import-fails</c>,
/// <c>fake:delete-fails</c>. A PLC compile reports a runtime group without a main safety block as TIA Portal did live. This
/// models shapes only; it is no evidence of Siemens behavior.
/// </summary>
sealed partial class PlcWriteRoundtripScenario
{
    private readonly HashSet<string> _uncompiled = new(Names);

    /// <summary>The runtime groups whose main safety block (not the IDB) is <paramref name="block"/>; empty when none or under noRoute.</summary>
    private IReadOnlyList<string> MainSafetyGroups(Plc plc, string block)
        => noRoute || !IsFailSafeCpu(plc)
            ? Array.Empty<string>()
            : PlcMainSafetyBlockSdRoute.MainBlockGroups(_runtimeGroups.Select(g => new PlcMainSafetyLink(g.Key, g.Value.Main, g.Value.Idb)), block);

    /// <summary>The caller-prefixed path of a block (the worker keeps the caller's PLC name in the route's paths).</summary>
    private static string CallerPath(string requested, string resolved)
        => BlockAddress.Parse(requested).IsDeterministic ? requested : resolved;

    /// <summary>update_block_logic (sd, or a bundle without .xml) onto a main safety block: the route instead of the SD import.</summary>
    private string UpdateMainSafetyBlockThroughRoute(WorkerRequest request, Plc plc, Group group, string block, string path, IReadOnlyList<string> groups)
    {
        var callerPath = CallerPath(request.BlockPath!, path);
        var documents = PlcMainSafetyBlockSdRoute.Documents(request.Content!) ?? Throw<(string, string?)>(WorkerFailureCategories.ValidationError, "The bundle has no .s7dcl document.");
        var steps = new RouteSteps(this, plc, group, block, path, request.Content!);
        try
        {
            var (_, info, warnings) = PlcMainSafetyBlockSdRoute.Run(steps, "update_block_logic", callerPath, block, callerPath[..callerPath.LastIndexOf('/')], groups,
                documents.Item1, documents.Item2);
            return JsonSerializer.Serialize(new WorkerResponse
            {
                Success = true, Payload = "Import succeeded.", BlockImportOutcome = completedOutcome(SourceFormatNames.Xml),
                Warnings = warnings.Count == 0 ? null : warnings, PlcDownloadOutcome = WorkerJson.SerializePayload(info),
            }, WorkerJson.Envelope);
        }
        catch (PlcMainSafetyBlockRouteException exception)
        {
            var started = exception.Info.MainImport != PlcMainSafetyBlockSdRoute.MainImports.NotStarted;
            return JsonSerializer.Serialize(new WorkerResponse
            {
                Success = false, FailureCategory = exception.Category, Error = exception.Message,
                BlockImportOutcome = started
                    ? new BlockImportOutcomeInfo { ImportStage = "unknown", ImportResultState = "unavailable", TargetMutationCommitted = null, CompileStage = "unavailable", FinalReadStage = "unavailable", ContentRelation = "unknown", TemporarySourceState = "not_applicable" }
                    : new BlockImportOutcomeInfo { ImportStage = "not_started", ImportResultState = "unavailable", TargetMutationCommitted = false, CompileStage = "not_started", FinalReadStage = "not_started", ContentRelation = "unknown", TemporarySourceState = "not_applicable" },
                PlcDownloadOutcome = WorkerJson.SerializePayload(exception.Info),
            }, WorkerJson.Envelope);
        }
    }

    /// <summary>create_block_from_document with overwrite onto a main safety block: the route; the block keeps its group.</summary>
    private PlcBlockEditResultInfo CreateOntoMainSafetyBlock(Plc plc, Group group, string block, string path, IReadOnlyList<string> groups, SimaticSdBlockDocument document,
        string content, List<string> messages)
    {
        var steps = new RouteSteps(this, plc, group, block, path, content);
        try
        {
            var (_, info, warnings) = PlcMainSafetyBlockSdRoute.Run(steps, SimaticSdMethods.CreateBlockFromDocument, path, block, path[..path.LastIndexOf('/')], groups,
                document.S7dcl, document.S7res);
            foreach (var warning in warnings) Console.Error.WriteLine(warning);
            messages.Add($"'{path}' is the main safety block of F-runtime group(s) {string.Join(", ", groups.Select(g => $"'{g}'"))}: the document went through the temporary F-block '{info.TemporaryBlockName}' and was imported as SimaticML (route {info.Route}).");
            return new PlcBlockEditResultInfo { Operation = SimaticSdMethods.CreateBlockFromDocument, Block = Identity(plc, path), Messages = messages, MainSafetyBlockRoute = info };
        }
        catch (PlcMainSafetyBlockRouteException exception)
        {
            throw new WorkerOperationException(exception.Category, exception.Message, plcDownloadOutcome: WorkerJson.SerializePayload(exception.Info));
        }
    }

    /// <summary>A minimal SimaticML export of a compiled block (the identity elements the route edits; no AutoNumber, as the live temporary export).</summary>
    private static string SimaticMlOf(string name, string type, string language, int number)
        => "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<Document>\r\n  <Engineering version=\"V21\" />\r\n"
           + $"  <SW.Blocks.{type} ID=\"0\">\r\n    <AttributeList>\r\n      <Interface><Sections xmlns=\"http://www.siemens.com/automation/Openness/SW/Interface/v5\" /></Interface>\r\n"
           + $"      <Name>{name}</Name>\r\n      <Number>{number}</Number>\r\n      <ProgrammingLanguage>{language}</ProgrammingLanguage>\r\n"
           + $"    </AttributeList>\r\n  </SW.Blocks.{type}>\r\n</Document>\r\n";

    /// <summary>get_block_content xml of an uncompiled block: only its SD documents, with the D15 warning (as the worker).</summary>
    private string ExportUncompiled(Plc plc, string block, string path, string requestedPath)
        => JsonSerializer.Serialize(new WorkerResponse
        {
            Success = true, Payload = ExportSd(plc, block, path), Warnings = new List<string> { SimaticMlExportNotes.NotExported(requestedPath, null) },
        }, WorkerJson.Envelope);

    /// <summary>compile_check in this fixture: compiles the block (or every block of the PLC); a runtime group without a main safety block is an error.</summary>
    private string Compile(WorkerRequest request)
    {
        if (request.BlockPath is not null)
        {
            var (blockPlc, _, block, path) = ResolveBlock(request.BlockPath);
            return Ok(CompileBlock(blockPlc, block, path, request.BlockPath));
        }

        var plc = FindPlc(request.PlcName);
        _uncompiled.RemoveWhere(p => p.StartsWith(plc.Software + "/", StringComparison.OrdinalIgnoreCase));
        var compiled = new PlcCompileInfo { PlcName = plc.Software, DeviceName = plc.Device, State = "Success" };
        if (IsFailSafeCpu(plc))
            foreach (var group in _runtimeGroups.Where(g => g.Value.Main is null))
                compiled.Messages.Add(new CompileMessageInfo { Description = $"The F-runtime group {group.Key} does not have a valid main safety block. (fake, as live 2026-11-02)", Path = group.Key, Severity = "Error" });
        compiled.ErrorCount = compiled.Messages.Count;
        compiled.State = compiled.ErrorCount > 0 ? "Error" : "Success";
        return Ok(new CompileCheckReport { Scope = "plc", OverallState = compiled.State, TotalErrorCount = compiled.ErrorCount, Plcs = { compiled } });
    }

    /// <summary>The compile of one block: an .s7dcl with the marker fake:compile-error fails, anything else compiles (and exports SimaticML afterwards).</summary>
    private CompileCheckReport CompileBlock(Plc plc, string block, string path, string requestedPath)
    {
        var content = _content.TryGetValue(Key(path, SourceFormatNames.Sd), out var sd) ? sd : string.Empty;
        var compiled = new PlcCompileInfo { PlcName = plc.Software, DeviceName = plc.Device, State = "Success" };
        if (content.Contains("fake:compile-error", StringComparison.Ordinal))
        {
            compiled.State = "Error";
            compiled.ErrorCount = 1;
            compiled.Messages.Add(new CompileMessageInfo { Description = "Network 1: invalid operand (fake)", Path = block, Severity = "Error" });
        }
        else
        {
            _uncompiled.Remove(path);
        }

        return new CompileCheckReport
        {
            Scope = "block", BlockPath = requestedPath, OverallState = compiled.State, TotalErrorCount = compiled.ErrorCount, Plcs = { compiled },
        };
    }

    private sealed class RouteSteps(PlcWriteRoundtripScenario scenario, Plc plc, Group group, string block, string path, string requestContent)
        : IPlcMainSafetyBlockSdRouteSteps<string>
    {
        private string TemporaryPath(string name) => path[..path.LastIndexOf('/')] + "/" + name;

        private bool Marked(string marker) => requestContent.Contains(marker, StringComparison.Ordinal);

        public bool TemporaryExists(string name) => AllBlocks(plc.Root).Any(b => Names.Equals(b.Name, name));

        public void ImportTemporary(string name, string s7dcl, string? s7res)
        {
            if (Marked("fake:temp-import-fails"))
                throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, "TIA Portal returned SIMATIC SD import state 'Failure': Syntax Error (fake)");
            var parsed = SimaticSdDocument.Parse(s7dcl);
            var type = BlockType(parsed.BlockKind);
            group.Blocks.Add((name, type, (parsed.IsFailSafe ? "F_" : string.Empty) + (parsed.PreferredLanguage ?? "FBD"),
                Enumerable.Range(1, 60000).First(n => !AllBlocks(plc.Root).Any(b => b.Type == type && b.Number == n))));
            scenario._content[Key(TemporaryPath(name), SourceFormatNames.Sd)] = $"--- FILE: {name}.s7dcl ---\n{s7dcl}" + (s7res is null ? string.Empty : $"\n--- FILE: {name}.s7res ---\n{s7res}");
            scenario._uncompiled.Add(TemporaryPath(name));
        }

        public CompileCheckReport CompileTemporary(string name) => scenario.CompileBlock(plc, name, TemporaryPath(name), TemporaryPath(name));

        public string? ExportTemporarySimaticMl(string name, out string? reason)
        {
            reason = null;
            if (scenario._uncompiled.Contains(TemporaryPath(name)) || Marked("fake:export-missing"))
            {
                reason = "the block is inconsistent (fake: TIA Portal exports no SimaticML of an uncompiled block)";
                return null;
            }

            var entry = AllBlocks(plc.Root).First(b => Names.Equals(b.Name, name));
            return SimaticMlOf(name, entry.Type, entry.Language, entry.Number);
        }

        public PlcMainSafetyBlockIdentity MainIdentity()
        {
            var entry = AllBlocks(plc.Root).First(b => Names.Equals(b.Name, block));
            // The live main safety block exported <AutoNumber>false</AutoNumber>.
            return new PlcMainSafetyBlockIdentity(entry.Number, false, PlcMainSafetyBlockSdRoute.IdentitySources.SimaticMlExport);
        }

        public string ImportMain(string simaticMl)
        {
            if (Marked("fake:main-import-fails"))
                throw new WorkerOperationException(WorkerFailureCategories.EngineeringRejected, "Block import did not complete.");
            // L3: the SimaticML import keeps the runtime group reference. The model stores the SimaticML and, as TIA Portal
            // would export the imported block, the caller's SD documents (the declared name is the main block's).
            scenario._content[Key(path, SourceFormatNames.Xml)] = simaticMl;
            var sd = requestContent.Contains("--- FILE:", StringComparison.Ordinal) ? requestContent : $"--- FILE: {block}.s7dcl ---\n{requestContent}";
            scenario._content[Key(path, SourceFormatNames.Sd)] = ImportFormMove.Replace(sd, "Move{ Card:=1; DisableENO:=TRUE }(");
            scenario._exports[path] = 0;
            scenario._written.Add(path);
            return "Import succeeded.";
        }

        public void DeleteTemporary(string name)
        {
            if (Marked("fake:delete-fails")) throw new InvalidOperationException("the block is locked (fake)");
            group.Blocks.RemoveAll(b => Names.Equals(b.Name, name));
            scenario._content.Remove(Key(TemporaryPath(name), SourceFormatNames.Sd));
            scenario._uncompiled.Remove(TemporaryPath(name));
        }

        public bool IsFatal(Exception exception) => false;

        public string CategoryOf(Exception exception)
            => exception is WorkerOperationException worker ? worker.FailureCategory : WorkerFailureCategories.WorkerOperationFailed;
    }
}
