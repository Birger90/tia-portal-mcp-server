using TiaMcpServer.Contracts.Block;

namespace TiaMcpServer.Worker;

/// <summary>Projects host-observed dispatch provenance into conservative import evidence.</summary>
internal static class BlockImportOutcomeSynthesizer
{
    public static BlockImportOutcomeInfo Synthesize(
        WorkerDispatchState dispatchState,
        string? normalizedFormatOrNull)
        => dispatchState == WorkerDispatchState.NotSent
            ? new BlockImportOutcomeInfo
            {
                ImportStage = "not_started",
                ImportResultState = "unavailable",
                TargetMutationCommitted = false,
                CompileStage = "not_started",
                FinalReadStage = "not_started",
                TemporarySourceState = normalizedFormatOrNull switch
                {
                    SourceFormatNames.Xml or SourceFormatNames.Sd => "not_applicable",
                    SourceFormatNames.Source => "not_created",
                    _ => "unknown"
                }
            }
            : new BlockImportOutcomeInfo
            {
                ImportStage = "unknown",
                ImportResultState = "unavailable",
                TargetMutationCommitted = null,
                CompileStage = "unavailable",
                FinalReadStage = "unavailable",
                TemporarySourceState = normalizedFormatOrNull is SourceFormatNames.Xml or SourceFormatNames.Sd
                    ? "not_applicable"
                    : "unknown"
            };
}
