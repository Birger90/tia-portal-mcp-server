using Siemens.Engineering;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Plc;

/// <summary>
/// The delete-and-import round trips of move_block, move_tag_table and copy_block, and the guarded
/// re-create of move_tag. Each runs inside one <c>TiaPortal.ExclusiveAccess(string)</c> and one
/// <c>ExclusiveAccess.Transaction(ITransactionSupport, string)</c> (the documented pattern for project
/// mutations: an exception before the transaction is disposed rolls it back, and
/// <c>CommitOnDispose()</c> is requested only after every step and its read-back succeeded). Only
/// when the rollback did not bring the original back is it re-imported from its export, and only when
/// that fails too is the export kept as a recovery file. That a block or tag table import runs inside
/// a transaction is not live-tested; an import TIA Portal refuses there fails the item with the
/// original unchanged.
/// </summary>
internal static class PlcRoundTrip
{
    internal enum TransactionState
    {
        Committed,
        RolledBack,
        OutcomeUnknown,
    }

    internal sealed class TransactionOutcome<T>
    {
        public TransactionState State { get; init; }

        public T? Result { get; init; }

        /// <summary>The exception that stopped the mutation, or the disposal failure.</summary>
        public Exception? Failure { get; init; }

        public List<string> Messages { get; } = new();
    }

    /// <summary>What a failed round trip moved or copied, and its export.</summary>
    internal sealed class Subject
    {
        public string Operation { get; init; } = string.Empty;

        public string ObjectKind { get; init; } = string.Empty;

        public string ObjectName { get; init; } = string.Empty;

        public string? PlcName { get; init; }

        public string SourcePath { get; init; } = string.Empty;

        public string? TargetPath { get; init; }

        /// <summary>The SimaticML export; null for copy_block, whose source is never changed.</summary>
        public string? Document { get; init; }
    }

    /// <summary>Recovery folder roots; tests replace them. Null means <see cref="PlcRecoveryStore.DefaultRoots"/>.</summary>
    internal static IReadOnlyList<(string Root, string Location)>? RecoveryRoots { get; set; }

    /// <summary>
    /// Runs <paramref name="mutate"/> in ExclusiveAccess plus a transaction. A failure to acquire
    /// either throws before anything changed. Everything after that is reported in the outcome.
    /// </summary>
    public static TransactionOutcome<T> Run<T>(TiaPortal portal, ProjectBase project, string description, Func<T> mutate)
    {
        var access = portal.ExclusiveAccess(description);
        try
        {
            var transaction = access.Transaction(project, description);
            Exception? failure = null;
            var result = default(T);
            try
            {
                result = mutate();
                if (transaction.CanCommit) transaction.CommitOnDispose();
                else failure = new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                    $"{description}: every step succeeded but TIA Portal reports that the transaction cannot be committed (CanCommit false), so it was rolled back.");
            }
            catch (NonRecoverableException)
            {
                TryDispose(transaction);
                throw;
            }
            catch (Exception exception) when (!PlcRecoveryStore.IsFatal(exception))
            {
                failure = exception;
            }

            try
            {
                transaction.Dispose();
            }
            catch (NonRecoverableException)
            {
                throw;
            }
            catch (Exception exception) when (!PlcRecoveryStore.IsFatal(exception))
            {
                var unknown = new TransactionOutcome<T> { State = TransactionState.OutcomeUnknown, Failure = failure ?? exception, Result = result };
                unknown.Messages.Add($"Disposing the transaction failed ({exception.GetType().Name}: {exception.Message}); whether TIA Portal committed it is unknown.");
                return unknown;
            }

            return failure is null
                ? new TransactionOutcome<T> { State = TransactionState.Committed, Result = result }
                : new TransactionOutcome<T> { State = TransactionState.RolledBack, Failure = failure };
        }
        finally
        {
            try
            {
                access.Dispose();
            }
            catch (Exception exception) when (exception is not NonRecoverableException && !PlcRecoveryStore.IsFatal(exception))
            {
                // Releasing ExclusiveAccess does not decide the commit; the transaction's disposal did.
                Console.Error.WriteLine($"Releasing ExclusiveAccess after '{description}' failed: {exception.Message}");
            }
        }
    }

    /// <summary>
    /// The failure of a move after its transaction: the original's state is probed; when it is gone it
    /// is re-imported, and when that fails its export is kept. The evidence travels with the failure.
    /// </summary>
    /// <param name="sourcePresent">Whether the original is in its source location now; throws when that cannot be read.</param>
    /// <param name="targetPresent">Whether the object is in the target location now.</param>
    /// <param name="restore">Re-imports the export into the source location.</param>
    /// <param name="existingDocumentPath">An export file already on disk that stays when no recovery file could be written.</param>
    public static WorkerOperationException MoveFailure<T>(TransactionOutcome<T> outcome, Subject subject,
        Func<bool> sourcePresent, Func<bool> targetPresent, Action restore, string? existingDocumentPath, out bool keepExistingDocument)
    {
        keepExistingDocument = false;
        var failure = outcome.Failure ?? new InvalidOperationException("The transaction ended without a recorded failure.");
        var evidence = Evidence(outcome, subject);
        var (source, target, probeError) = Probe(sourcePresent, targetPresent);
        string summary;
        if (source == true)
        {
            evidence.OriginalState = PlcBlockEditOriginalStates.Unchanged;
            summary = outcome.State == TransactionState.RolledBack
                ? $"The transaction was rolled back; '{subject.SourcePath}' is unchanged."
                : $"'{subject.SourcePath}' is still in its source location.";
        }
        else if (source == false && target == true)
        {
            evidence.OriginalState = PlcBlockEditOriginalStates.MovedToTarget;
            summary = $"'{subject.ObjectName}' is in '{subject.TargetPath}' and no longer in its source location; inspect it before retrying.";
        }
        else if (source == false)
        {
            try
            {
                restore();
                evidence.OriginalState = PlcBlockEditOriginalStates.RestoredByReimport;
                summary = $"The rollback did not bring '{subject.ObjectName}' back; it was re-imported from its export into '{subject.SourcePath}'.";
            }
            catch (NonRecoverableException)
            {
                throw;
            }
            catch (Exception restoreFailure) when (!PlcRecoveryStore.IsFatal(restoreFailure))
            {
                evidence.OriginalState = PlcBlockEditOriginalStates.Missing;
                summary = $"'{subject.ObjectName}' is no longer in the project and re-importing its export failed ({Describe(restoreFailure)}). "
                    + Keep(evidence, subject, failure, existingDocumentPath, out keepExistingDocument);
            }
        }
        else
        {
            evidence.OriginalState = PlcBlockEditOriginalStates.Unknown;
            summary = $"Where '{subject.ObjectName}' is now could not be read ({probeError}); it was not re-imported. "
                + Keep(evidence, subject, failure, existingDocumentPath, out keepExistingDocument);
        }

        evidence.Messages.Add(summary);
        return Wrap(failure, summary, WorkerJson.SerializePayload(evidence));
    }

    /// <summary>A failed copy_block: its source never changed; the evidence says whether a copy exists.</summary>
    public static WorkerOperationException CopyFailure<T>(TransactionOutcome<T> outcome, Subject subject, Func<bool> copyPresent)
    {
        var failure = outcome.Failure ?? new InvalidOperationException("The transaction ended without a recorded failure.");
        var evidence = Evidence(outcome, subject);
        bool? present;
        string? probeError = null;
        try
        {
            present = copyPresent();
        }
        catch (Exception exception) when (exception is not NonRecoverableException && !PlcRecoveryStore.IsFatal(exception))
        {
            present = null;
            probeError = Describe(exception);
        }

        evidence.OriginalState = present switch
        {
            false => PlcBlockEditOriginalStates.CopyAbsent,
            true => PlcBlockEditOriginalStates.CopyPresent,
            null => PlcBlockEditOriginalStates.Unknown,
        };
        var summary = present switch
        {
            false => $"No copy was left in '{subject.TargetPath}'; '{subject.SourcePath}' was not changed.",
            true => $"A block named '{subject.ObjectName}' is in '{subject.TargetPath}' despite the failure; inspect it before retrying. '{subject.SourcePath}' was not changed.",
            null => $"Whether a copy was left in '{subject.TargetPath}' could not be read ({probeError}); '{subject.SourcePath}' was not changed.",
        };
        evidence.Messages.Add(summary);
        return Wrap(failure, summary, WorkerJson.SerializePayload(evidence));
    }

    /// <summary>A failed in-transaction re-create (move_tag): the original failure with the rollback stated.</summary>
    public static WorkerOperationException RolledBackFailure<T>(TransactionOutcome<T> outcome, Func<string> describeState)
    {
        var failure = outcome.Failure ?? new InvalidOperationException("The transaction ended without a recorded failure.");
        string state;
        try
        {
            state = describeState();
        }
        catch (Exception exception) when (exception is not NonRecoverableException && !PlcRecoveryStore.IsFatal(exception))
        {
            state = $"The state after it could not be read ({Describe(exception)}); inspect the tag tables.";
        }

        var note = outcome.State == TransactionState.RolledBack
            ? "TIA Portal rolled the transaction back; read back afterwards: " + state
            : string.Join(" ", outcome.Messages) + " " + state;
        return Wrap(failure, note, null);
    }

    /// <summary>
    /// move_tag's guarded re-create (<see cref="TagMutationService.MoveTag"/>, unchanged) inside one
    /// ExclusiveAccess transaction: a failure at any step rolls back the temporary tag, the source
    /// delete and the rename together, and the failure states the tag tables' state afterwards.
    /// </summary>
    public static PlcTagMoveResultInfo MoveTag(TiaPortal portal, ProjectBase project, PlcTagMoveRequestInfo move, Func<PlcTagMoveResultInfo> run)
    {
        var outcome = Run(portal, project, $"move_tag '{move.Name}' to '{move.TargetTableName}'", run);
        if (outcome.State == TransactionState.Committed) return outcome.Result!;
        throw RolledBackFailure(outcome, () =>
        {
            bool Present(string table, string? folder, string tag)
            {
                try
                {
                    TagTargetResolver.Resolve(project, move.PlcName, table, folder, tag);
                    return true;
                }
                catch (WorkerOperationException missing) when (missing.FailureCategory == WorkerFailureCategories.TargetNotFound)
                {
                    return false;
                }
            }

            var source = Present(move.TableName, move.FolderPath, move.Name);
            var temporary = Present(move.TargetTableName, move.TargetFolderPath, move.TemporaryName);
            var moved = Present(move.TargetTableName, move.TargetFolderPath, move.Name);
            return source && !temporary && !moved
                ? $"tag '{move.Name}' is in '{move.TableName}' again and neither it nor the temporary tag '{move.TemporaryName}' is in '{move.TargetTableName}'; nothing changed."
                : $"read back after the rollback: '{move.Name}' in '{move.TableName}' {(source ? "present" : "absent")}, '{move.TemporaryName}' in '{move.TargetTableName}' {(temporary ? "present" : "absent")}, "
                    + $"'{move.Name}' in '{move.TargetTableName}' {(moved ? "present" : "absent")}; inspect the tag tables before retrying.";
        });
    }

    private static PlcBlockEditFailureInfo Evidence<T>(TransactionOutcome<T> outcome, Subject subject)
    {
        var evidence = new PlcBlockEditFailureInfo
        {
            Operation = subject.Operation,
            ObjectKind = subject.ObjectKind,
            ObjectName = subject.ObjectName,
            SourcePath = subject.SourcePath,
            TargetPath = subject.TargetPath,
            Transaction = outcome.State == TransactionState.OutcomeUnknown ? PlcBlockEditTransactionStates.OutcomeUnknown : PlcBlockEditTransactionStates.RolledBack,
        };
        evidence.Messages.AddRange(outcome.Messages);
        return evidence;
    }

    private static (bool? Source, bool? Target, string? Error) Probe(Func<bool> sourcePresent, Func<bool> targetPresent)
    {
        try
        {
            var source = sourcePresent();
            bool? target = null;
            if (!source)
            {
                try
                {
                    target = targetPresent();
                }
                catch (Exception exception) when (exception is not NonRecoverableException && !PlcRecoveryStore.IsFatal(exception))
                {
                    return (null, null, Describe(exception));
                }
            }

            return (source, target, null);
        }
        catch (Exception exception) when (exception is not NonRecoverableException && !PlcRecoveryStore.IsFatal(exception))
        {
            return (null, null, Describe(exception));
        }
    }

    /// <summary>Writes the recovery folder; when that fails an export already on disk is kept instead.</summary>
    private static string Keep(PlcBlockEditFailureInfo evidence, Subject subject, Exception failure, string? existingDocumentPath, out bool keepExistingDocument)
    {
        keepExistingDocument = false;
        if (subject.Document is null)
        {
            evidence.RecoveryError = "No export of the original was taken.";
            return "No export of it is available.";
        }

        var saved = PlcRecoveryStore.Save(new PlcRecoveryRecord
        {
            Operation = subject.Operation,
            ObjectKind = subject.ObjectKind,
            ObjectName = subject.ObjectName,
            PlcName = subject.PlcName,
            SourcePath = subject.SourcePath,
            TargetPath = subject.TargetPath,
            Failure = Describe(failure),
        }, subject.Document, RecoveryRoots);
        evidence.RecoveryError = saved.Error;
        if (saved.File is { } file)
        {
            evidence.Recovery = file;
            return $"Its SimaticML export was kept at '{file.DocumentPath}' (details in '{file.MetadataPath ?? "(sidecar not written)"}'); import it into '{subject.SourcePath}' to restore it.";
        }

        if (existingDocumentPath is not null && File.Exists(existingDocumentPath))
        {
            keepExistingDocument = true;
            evidence.Recovery = new PlcRecoveryFileInfo
            {
                Directory = Path.GetDirectoryName(existingDocumentPath) ?? string.Empty,
                DocumentPath = existingDocumentPath,
                MetadataPath = null,
                Location = PlcRecoveryStore.TempLocation,
            };
            return $"No recovery folder could be written ({saved.Error}); the export stays at '{existingDocumentPath}', which is not cleaned up; copy it somewhere safe and import it into '{subject.SourcePath}'.";
        }

        return $"No recovery folder could be written ({saved.Error}); the export is lost and the object is no longer in the project.";
    }

    /// <summary>The original failure's category and Siemens evidence, with the round trip's outcome appended.</summary>
    internal static WorkerOperationException Wrap(Exception failure, string note, string? evidence)
        => failure switch
        {
            EngineeringException engineering => new WorkerOperationException(
                WorkerExecution.Classify(engineering, mutates: true),
                WorkerExecution.FailureMessage(engineering, WorkerExecution.Classify(engineering, mutates: true)) + " " + note,
                engineeringError: WorkerExecution.Describe(engineering),
                plcDownloadOutcome: evidence),
            WorkerOperationException worker => new WorkerOperationException(worker.FailureCategory, worker.Message + " " + note, worker.Warnings,
                engineeringError: worker.EngineeringError, plcDownloadOutcome: evidence),
            _ => new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, $"{failure.GetType().Name}: {failure.Message} {note}",
                plcDownloadOutcome: evidence),
        };

    private static string Describe(Exception exception) => $"{exception.GetType().Name}: {exception.Message}";

    private static void TryDispose(IDisposable disposable)
    {
        try
        {
            disposable.Dispose();
        }
        catch (Exception exception) when (!PlcRecoveryStore.IsFatal(exception))
        {
            // The Portal is already lost; the original NonRecoverableException is what the caller reports.
        }
    }
}
