using Siemens.Engineering;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Openness.PlcOnline;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker;

internal static partial class Program
{
    private static class PlcWorkerDispatch
    {
        public static WorkerResponse GetBlockContent(WorkerRequest request)
        {
            if (string.IsNullOrEmpty(request.BlockPath))
            {
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "BlockPath is required.");
            }

            var format = NormalizeBlockFormat(request.Format);
            var withDependencies = request.WithDependencies == true;

            return WithEngineeringRoot(request, project =>
            {
                var content = BlockExporter.Export(project, request.BlockPath!, format, withDependencies);
                return RawPayload(content, SourceReadWarnings.ForExport(withDependencies, format, content));
            });
        }

        public static WorkerResponse UpdateBlockLogic(WorkerRequest request)
        {
            var normalizedFormat = BlockUpdateOutcomeDecorator.NormalizeFormatOrNull(request.Format);
            var importerEntered = false;
            var routed = false;
            try
            {
                if (string.IsNullOrEmpty(request.BlockPath))
                {
                    throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "BlockPath is required.");
                }

                if (string.IsNullOrEmpty(request.Content))
                {
                    throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "Content is required.");
                }

                normalizedFormat = NormalizeBlockFormat(request.Format);

                var response = WithEngineeringRoot(request, project =>
                {
                    // F-runtime group references of the block are recorded before and checked (and restored) after every import
                    // route, also after a failed one (live 2026-11-02, E7: the SD import dropped MainSafetyBlockName). Reading them
                    // never throws (except a lost Portal) and changes nothing, so the importer is not entered yet.
                    var links = Openness.Safety.PlcMainSafetyBlockLinkGuard.Before(project, request.BlockPath!);
                    BlockImportResult result;
                    try
                    {
                        // An SD document for the main safety block of an F-runtime group goes through a temporary F-block and is
                        // imported as SimaticML (live 2026-11-02, L1-L4); every other import is unchanged.
                        BlockImporter.RequireImportOptionsFitFormat(normalizedFormat, request.SwImportOptions, request.DocumentImportOption);
                        if (Openness.Block.MainSafetyBlockSdUpdate.Applies(project, request.BlockPath!, request.Content!, normalizedFormat) is { } target)
                        {
                            if (request.SwImportOptions is { Count: > 0 })
                                throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                                    "swImportOptions apply to the SimaticML route only; this SIMATIC SD document goes through the main safety block route. Nothing was imported.");
                            // The route enters the importer itself (its own preflight carries pre-target evidence); routed counts as entered below.
                            routed = true;
                            var routedResponse = Openness.Block.MainSafetyBlockSdUpdate.Run(project, target, request.BlockPath!, request.Content!, normalizedFormat, request.ExpectedContentHash,
                                request.DocumentImportOption);
                            links?.After();
                            return routedResponse;
                        }

                        importerEntered = true;
                        result = BlockImporter.Import(project, request.BlockPath!, request.Content!, normalizedFormat, request.ExpectedContentHash,
                            request.SwImportOptions, request.DocumentImportOption);
                    }
                    catch (Exception exception) when (links is not null && exception is not (NonRecoverableException or EngineeringObjectDisposedException))
                    {
                        links.After();
                        throw;
                    }

                    links?.After();
                    return RawPayload(result.Payload, result.Warnings, result.Outcome);
                });
                // The route's response carries its own message and evidence (PlcDownloadOutcome); the generic decoration would drop them.
                return response.Success || routed && response.PlcDownloadOutcome is not null
                    ? response
                    : BlockUpdateOutcomeDecorator.Decorate(response, normalizedFormat, importerEntered || routed);
            }
            catch (WorkerOperationException exception)
            {
                return BlockUpdateOutcomeDecorator.Decorate(exception, normalizedFormat, importerEntered || routed);
            }
            catch (Exception exception)
            {
                return BlockUpdateOutcomeDecorator.Decorate(exception, normalizedFormat, importerEntered || routed);
            }
        }

        public static WorkerResponse GetTypeContent(WorkerRequest request)
        {
            if (string.IsNullOrEmpty(request.TypePath))
            {
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "TypePath is required.");
            }

            var format = NormalizeFormat(request.Format, SourceFormatNames.Source);
            var withDependencies = request.WithDependencies == true;

            return WithEngineeringRoot(request, project =>
            {
                var content = PlcTypeExporter.Export(project, request.TypePath!, format, withDependencies);
                return RawPayload(content, SourceReadWarnings.ForExport(withDependencies, format, content));
            });
        }

        public static WorkerResponse UpdateTypeContent(WorkerRequest request)
        {
            if (string.IsNullOrEmpty(request.TypePath))
            {
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "TypePath is required.");
            }

            if (string.IsNullOrEmpty(request.Content))
            {
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "Content is required.");
            }

            var format = NormalizeFormat(request.Format, SourceFormatNames.Source);
            if (request.SwImportOptions is { Count: > 0 } && format != SourceFormatNames.Xml)
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                    $"swImportOptions apply to format xml (SimaticML) only, not to format {format}. Nothing was imported.");

            return WithEngineeringRoot(request, project => Success(
                PlcTypeImporter.Import(project, request.TypePath!, request.Content!, format, request.ExpectedContentHash, request.SwImportOptions)));
        }

        // Direct worker callers retain each operation's default: XML for blocks, source for types.
        private static string NormalizeFormat(string? format, string fallback)
        {
            if (!SourceFormatNames.TryNormalize(format, fallback, out var normalized, out var error))
            {
                throw new WorkerOperationException(
                    WorkerFailureCategories.ValidationError,
                    error ?? "Invalid format.");
            }

            return normalized;
        }

        // Fork: block content operations also accept SIMATIC SD (SourceFormatNames.TryNormalizeBlock); types keep NormalizeFormat.
        private static string NormalizeBlockFormat(string? format)
        {
            if (!SourceFormatNames.TryNormalizeBlock(format, SourceFormatNames.Xml, out var normalized, out var error))
            {
                throw new WorkerOperationException(
                    WorkerFailureCategories.ValidationError,
                    error ?? "Invalid format.");
            }

            return normalized;
        }

        public static WorkerResponse ListTagTables(WorkerRequest request)
        {
            return WithEngineeringRoot(request, project => Success(TagTableReader.ReadInventory(
                project, request.PlcName, request.TableName, request.FolderPath)));
        }

        public static WorkerResponse CompileCheck(WorkerRequest request)
        {
            return WithEngineeringRoot(request, project => Success(CompileChecker.Compile(project, request.PlcName, request.BlockPath, request.CompileScope)));
        }

        public static WorkerResponse ListDownloadTargets(WorkerRequest request)
            => WithEngineeringRoot(request, project => Success(PlcDownloadService.ListTargets(project, request.PlcName)));

        /// <summary>
        /// Never inside a Transaction or ExclusiveAccess: Siemens forbids download in a transaction. Until
        /// Download returns and the evidence is collected, every TIA Portal confirmation is answered by the
        /// request's <c>portalConfirmations</c> policy (No by default, Yes for accept; not the session-wide
        /// answer) and recorded as evidence; a confirmation raised after the scope closed (for example after
        /// an early return of Download) keeps the call's answer until the next request starts and is reported as late
        /// on that request's response (see PlcOnlineConfirmationScope.BeginRequest). The scope also
        /// closes in a finally block.
        /// </summary>
        public static WorkerResponse DownloadPlc(WorkerRequest request)
        {
            var download = request.PlcDownload ?? throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError, "plc_download requires the resolved download request.");
            var confirmations = new PlcDownloadPortalConfirmationLog();
            var accept = string.Equals(download.PortalConfirmations, PlcDownloadPolicy.PortalConfirmationPolicies.Accept, StringComparison.Ordinal);
            // The download closes the scope itself before it copies the evidence, so every confirmation
            // answered in the scope is recorded; this using is the safety net for exits without evidence.
            using (var scope = _sharedSession.AnswerConfirmations(accept, (args, answer) =>
                       confirmations.Record(args.GetType().FullName, PlcDownloadService.ReadConfirmationText(args),
                           answer == ConfirmationResult.Yes ? PlcDownloadPolicy.PortalConfirmationAnswers.Yes : PlcDownloadPolicy.PortalConfirmationAnswers.No),
                       owner: request.Method))
            {
                confirmations.AttachScope(scope);
                return WithEngineeringRoot(request, project => Success(PlcDownloadService.Download(project, download, confirmations)));
            }
        }

        public static WorkerResponse EditPlcBlock(WorkerRequest request)
        {
            return WithActiveContext(request, session => Success(PlcBlockEditDispatch.Run(
                session.TiaPortal!, session.EngineeringRoot!, request.Method, request.PlcBlockEdit ?? new PlcBlockEditRequestInfo())));
        }

        /// <summary>
        /// Compare, online state, plc_tia_online, plc_station_upload, watch/force tables and update_program.
        /// Every mutation and online action requires the worker-internal confirmation fence like the other
        /// writes and plc_download; none runs inside a Transaction or ExclusiveAccess.
        /// </summary>
        public static WorkerResponse DispatchPlcOnline(WorkerRequest request)
        {
            if (!PlcOnlineConfirmationScope.AnswersPortalConfirmations(request.Method))
                return WithActiveContext(request, session => RawPayload(PlcOnlineWorkerDispatch.Invoke(session.EngineeringRoot!, request, portal: session.TiaPortal)));

            // Like plc_download: until the action's evidence is collected, every TIA Portal confirmation is
            // answered by the request's portalConfirmations policy (compare_to_online: always No) and recorded;
            // the services close the scope before they copy the evidence (it then lingers until the next
            // request starts), this using is the safety net.
            var (confirmations, scope) = PlcOnlineConfirmationScope.Open(_sharedSession, request.Method, request.PlcOnline, PlcDownloadService.ReadConfirmationText);
            using (scope)
            {
                return WithEngineeringRoot(request, project => RawPayload(PlcOnlineWorkerDispatch.Invoke(project, request, confirmations,
                    portal: _sharedSession.TiaPortal, deferred: PlcOnlineConfirmationScope.NextRequest)));
            }
        }

        public static WorkerResponse CreateTagTable(WorkerRequest request)
        {
            return TagMutation(request, project =>
                TagMutationService.CreateTagTable(project, request.PlcName, request.TableName!, request.FolderPath));
        }

        public static WorkerResponse DeleteTagTable(WorkerRequest request)
        {
            return TagMutation(request, project =>
                TagMutationService.DeleteTagTable(project, request.PlcName, request.TableName!, request.FolderPath));
        }

        public static WorkerResponse CreateTag(WorkerRequest request)
        {
            return TagMutation(request, project =>
                TagMutationService.CreateTag(
                    project,
                    request.PlcName,
                    request.TableName!,
                    request.FolderPath,
                    request.Name!,
                    request.DataType!,
                    request.LogicalAddress,
                    request.Comment,
                    request.CommentCulture));
        }

        public static WorkerResponse UpdateTag(WorkerRequest request)
        {
            return TagMutation(request, project =>
                TagMutationService.UpdateTag(
                    project,
                    request.PlcName,
                    request.TableName!,
                    request.FolderPath,
                    request.Name!,
                    request.NewName,
                    request.DataType,
                    request.LogicalAddress,
                    request.ExternalAccessible,
                    request.ExternalVisible,
                    request.ExternalWritable,
                    request.Comment,
                    request.CommentCulture));
        }

        public static WorkerResponse DeleteTag(WorkerRequest request)
        {
            return TagMutation(request, project =>
                TagMutationService.DeleteTag(
                    project,
                    request.PlcName,
                    request.TableName!,
                    request.FolderPath,
                    request.Name!));
        }

        public static WorkerResponse CreateUserConstant(WorkerRequest request)
        {
            return TagMutation(request, project =>
                TagMutationService.CreateUserConstant(
                    project,
                    request.PlcName,
                    request.TableName!,
                    request.FolderPath,
                    request.Name!,
                    request.DataType!,
                    request.Value!));
        }

        public static WorkerResponse UpdateUserConstant(WorkerRequest request)
        {
            return TagMutation(request, project =>
                TagMutationService.UpdateUserConstant(
                    project,
                    request.PlcName,
                    request.TableName!,
                    request.FolderPath,
                    request.Name!,
                    request.DataType,
                    request.Value));
        }

        public static WorkerResponse DeleteUserConstant(WorkerRequest request)
        {
            return TagMutation(request, project =>
                TagMutationService.DeleteUserConstant(
                    project,
                    request.PlcName,
                    request.TableName!,
                    request.FolderPath,
                    request.Name!));
        }

        public static WorkerResponse CreateBlock(WorkerRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.BlockPath))
            {
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "BlockPath is required.");
            }

            if (string.IsNullOrWhiteSpace(request.BlockType))
            {
                throw new WorkerOperationException(
                    WorkerFailureCategories.ValidationError,
                    "BlockType is required. Valid values: FB, FC, OB, GlobalDB.");
            }

            return WithEngineeringRoot(request, project => Success(
                BlockMutationService.CreateBlock(
                    project,
                    request.BlockPath!,
                    request.BlockType!,
                    request.Language,
                    request.OBEventClass)));
        }

        public static WorkerResponse DeleteBlock(WorkerRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.BlockPath))
            {
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "BlockPath is required.");
            }

            return WithEngineeringRoot(request, project => Success(
                BlockMutationService.DeleteBlock(project, request.BlockPath!)));
        }

        public static WorkerResponse CreateBlockGroup(WorkerRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.BlockPath))
            {
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "BlockPath is required.");
            }

            return WithEngineeringRoot(request, project => Success(
                BlockMutationService.CreateBlockGroup(project, request.BlockPath!)));
        }

        public static WorkerResponse DeleteBlockGroup(WorkerRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.BlockPath))
            {
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "BlockPath is required.");
            }

            return WithEngineeringRoot(request, project => Success(
                BlockMutationService.DeleteBlockGroup(project, request.BlockPath!)));
        }

        private static WorkerResponse TagMutation(WorkerRequest request, Func<ProjectBase, TagMutationResultInfo> mutate)
        {
            return WithEngineeringRoot(request, project => Success(mutate(project)));
        }
    }
}
