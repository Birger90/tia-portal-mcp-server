using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc.Interface;
using TiaMcpServer.Contracts.Plc.Safety;
using TiaMcpServer.Contracts.Plc.SimaticSd;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Safety;

/// <summary>
/// The Openness side of <see cref="PlcMainSafetyBlockSdRoute"/>: an SD-document write onto the main safety block of an
/// F-runtime group is never imported onto that block (live TIA Portal V21, 2026-11-02, L1: the SIMATIC SD import drops the
/// group's MainSafetyBlockName, and L2: it cannot be re-assigned through Openness). The document goes through a temporary
/// F-block in the main block's group, is compiled there, exported as SimaticML and imported onto the main block through the
/// existing update_block_logic xml path (L3: the SimaticML route keeps the reference); the steps, the cleanup and the evidence
/// are the shared <see cref="PlcMainSafetyBlockSdRoute.Run{TImport}"/>. The steps run one after another without a transaction,
/// as the live proof did: a transaction would roll the main import back together with the temporary block, and compile and
/// export inside an open transaction are not verified. Not live-tested as one call.
/// </summary>
internal static class PlcMainSafetyBlockSdRouteWorker
{
    /// <summary>The main safety block an SD write targets: where it is and which runtime groups name it.</summary>
    internal sealed class Target(string? plcName, PlcSoftware software, LocatedBlock located, IReadOnlyList<string> runtimeGroups)
    {
        /// <summary>The PLC as the caller addressed it (null: the only PLC).</summary>
        public string? PlcName { get; } = plcName;

        public PlcSoftware Software { get; } = software;

        public LocatedBlock Located { get; } = located;

        public IReadOnlyList<string> RuntimeGroups { get; } = runtimeGroups;

        /// <summary>The block's group path, with the caller's PLC name, so the compile resolves it like the caller's path.</summary>
        public string GroupPath
        {
            get
            {
                var path = LocatedBlock.GroupPath(Located.Owner, Located.Folders);
                return PlcName is null || !path.StartsWith(Software.Name + "/", StringComparison.Ordinal) ? path : PlcName + path.Substring(Software.Name.Length);
            }
        }

        public string BlockPath => GroupPath + "/" + Located.Block.Name;
    }

    private static bool IsFatal(Exception exception)
        => exception is NonRecoverableException or EngineeringObjectDisposedException or OutOfMemoryException;

    /// <summary>
    /// The target when <paramref name="blockPath"/> is the main safety block (MainSafetyBlockName, not the instance DB) of an
    /// F-runtime group; null otherwise, or when that cannot be read (the direct route then runs and the link guard reports).
    /// </summary>
    public static Target? Find(ProjectBase project, string blockPath)
    {
        try
        {
            var address = BlockAddress.Parse(blockPath);
            var plc = PlcSoftwareLocator.FindUnique(project, address.PlcName);
            return Find(project, address.PlcName, plc.Software, BlockLocator.Locate(plc.Software, address));
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            // The import's own preflight reports an unresolvable target.
            return null;
        }
    }

    /// <summary>The target for an existing block <paramref name="located"/> (create_block_from_document with overwrite).</summary>
    public static Target? Find(ProjectBase project, string? plcName, PlcSoftware software, LocatedBlock located)
    {
        try
        {
            string? language = null;
            try
            {
                language = located.Block.ProgrammingLanguage.ToString();
            }
            catch (EngineeringException exception) when (!IsFatal(exception))
            {
                // Unknown language: looked up like an F-block.
            }

            if (language is not null && !PlcSafetyPolicy.IsFailSafeLanguage(language)) return null;
            var plc = PlcSoftwareLocator.FindUnique(project, plcName);
            if (PlcSafetyService.Administration(plc.HostDeviceItem).Administration is not { } administration) return null;
            var groups = PlcMainSafetyBlockSdRoute.MainBlockGroups(PlcSafetyService.ReadLinks(administration), located.Block.Name);
            return groups.Count == 0 ? null : new Target(plcName, software, located, groups);
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            Console.Error.WriteLine($"The F-runtime groups of PLC '{plcName}' could not be read before the SD write onto '{located.CanonicalPath}' ({OneLine(exception.Message)}); "
                + "whether the block is a main safety block is unknown, so the SD document is imported directly. Inspect plc_read get_safety_administration afterwards.");
            return null;
        }
    }

    /// <summary>Runs the route for <paramref name="target"/> with the SD documents; the main import is update_block_logic xml.</summary>
    /// <param name="documentImportOption">Audit N7: the caller's culture member, added to Override for the temporary F-block's import.</param>
    public static (BlockImportResult Result, PlcMainSafetyBlockRouteInfo Info, List<string> Warnings) Run(
        ProjectBase project, Target target, string operation, string s7dcl, string? s7res, string? documentImportOption = null)
        => PlcMainSafetyBlockSdRoute.Run(new Steps(project, target, documentImportOption), operation, target.BlockPath, target.Located.Block.Name, target.GroupPath,
            target.RuntimeGroups, s7dcl, s7res);

    private static string OneLine(string text) => text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');

    private sealed class Steps(ProjectBase project, Target target, string? documentImportOption) : IPlcMainSafetyBlockSdRouteSteps<BlockImportResult>
    {
        private PlcBlockComposition Blocks => target.Located.Group.Blocks;

        private string TemporaryPath(string name) => target.GroupPath + "/" + name;

        public bool TemporaryExists(string name)
        {
            try
            {
                BlockLocator.Locate(target.Software, BlockAddress.Parse(target.Software.Name + "/" + name));
                return true;
            }
            catch (WorkerOperationException exception) when (exception.FailureCategory == WorkerFailureCategories.TargetNotFound)
            {
                return false;
            }
            catch (WorkerOperationException exception) when (exception.FailureCategory == WorkerFailureCategories.TargetAmbiguous)
            {
                return true;
            }
        }

        public void ImportTemporary(string name, string s7dcl, string? s7res)
        {
            var directory = Path.Combine(Path.GetTempPath(), "tia-mcp-sd-route-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                File.WriteAllBytes(Path.Combine(directory, name + SimaticSdText.BundleExtension), SimaticSdFileText.Encode(s7dcl));
                if (s7res is not null)
                    File.WriteAllBytes(Path.Combine(directory, name + SimaticSdText.ResourceExtension), SimaticSdFileText.Encode(s7res));
                var result = Blocks.ImportFromDocuments(new DirectoryInfo(directory), name, Block.BlockImporter.DocumentImportOptions(documentImportOption));
                var messages = SimaticSdImportMessages.Read(result);
                foreach (var message in messages) Console.Error.WriteLine(BlockImportDiagnosticSanitizer.SimaticSdMessagePrefix + OneLine(message));
                if (result.State != DocumentResultState.Success)
                    throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                        $"TIA Portal returned SIMATIC SD import state '{result.State}'" + (messages.Count == 0 ? string.Empty : ": " + string.Join(" | ", messages.Select(OneLine))));
                BlockExportLedger.Shared.RecordWrite(target.Software.Name, name);
                if (Blocks.Find(name) is null)
                    throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                        $"ImportFromDocuments returned success, but '{target.GroupPath}' holds no block named '{name}' afterwards");
            }
            finally
            {
                TemporaryDirectoryCleanup.TryDeleteTempDirectory(directory);
            }
        }

        public CompileCheckReport CompileTemporary(string name) => CompileChecker.Compile(project, target.PlcName, TemporaryPath(name));

        public string? ExportTemporarySimaticMl(string name, out string? reason)
        {
            reason = null;
            var block = Blocks.Find(name);
            if (block is null)
            {
                reason = $"no block '{name}' was found in '{target.GroupPath}'";
                return null;
            }

            BlockExportLedger.Shared.RecordExport(target.Software.Name, name);
            try
            {
                return BlockExporter.ExportSimaticMl(block, name);
            }
            catch (Exception exception) when (OpennessExceptions.IsRecoverable(exception))
            {
                reason = OneLine(exception.Message);
                return null;
            }
        }

        public PlcMainSafetyBlockIdentity MainIdentity()
        {
            var bundle = BlockExporter.Export(project, target.BlockPath, SourceFormatNames.Xml);
            if (PlcBlockBundleText.TryGetSimaticMl(bundle, out _, out var xml))
            {
                var values = SimaticMlBlockIdentity.Read(xml);
                if (int.TryParse(values.Number, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number))
                    return new PlcMainSafetyBlockIdentity(number,
                        values.AutoNumber is null ? LiveAutoNumber() : bool.TryParse(values.AutoNumber, out var auto) ? auto : null,
                        PlcMainSafetyBlockSdRoute.IdentitySources.SimaticMlExport);
            }

            // The main safety block exported no SimaticML (it is inconsistent itself): its live attributes.
            return new PlcMainSafetyBlockIdentity(target.Located.Block.Number, LiveAutoNumber(), PlcMainSafetyBlockSdRoute.IdentitySources.LiveAttributes);
        }

        private bool? LiveAutoNumber()
        {
            try
            {
                return ((IEngineeringObject)target.Located.Block).GetAttribute("AutoNumber") as bool?;
            }
            catch (EngineeringException exception) when (!IsFatal(exception))
            {
                return null;
            }
        }

        public BlockImportResult ImportMain(string simaticMl)
        {
            var hash = ContentHashRules.Compute(SourceFormatNames.Xml, BlockExporter.Export(project, target.BlockPath, SourceFormatNames.Xml));
            try
            {
                return BlockImporter.Import(project, target.BlockPath, simaticMl, SourceFormatNames.Xml, hash);
            }
            catch (WorkerOperationException exception)
            {
                // The update_block_logic diagnostics policy: closed summaries only; the outcome evidence is kept.
                throw new WorkerOperationException(exception.FailureCategory,
                    BlockImportDiagnosticSanitizer.Failure(BlockImportDiagnosticContext.Import, exception),
                    BlockImportDiagnosticSanitizer.SanitizeWarnings(exception.Warnings), exception.BlockImportOutcome,
                    engineeringError: exception.EngineeringError);
            }
        }

        public void DeleteTemporary(string name)
        {
            var block = Blocks.Find(name) ?? throw new InvalidOperationException($"no block '{name}' was found in '{target.GroupPath}'");
            block.Delete();
        }

        public bool IsFatal(Exception exception) => PlcMainSafetyBlockSdRouteWorker.IsFatal(exception);

        public string CategoryOf(Exception exception) => exception switch
        {
            WorkerOperationException worker => worker.FailureCategory,
            EngineeringException engineering => WorkerExecution.Classify(engineering, mutates: true),
            SimaticSdException sd => sd.Category,
            _ => WorkerFailureCategories.WorkerOperationFailed,
        };
    }
}
