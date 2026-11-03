using System.Text.Json;
using TiaMcpServer.Contracts.CrossReferences;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.CrossReferences;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.ProjectTree;
using TiaMcpServer.Worker;

namespace TiaMcpServer.PlcAnalysis;

/// <summary>
/// The evidence reads of the analysis operations within one plc_read call: one project-tree snapshot
/// and one cross-reference report per target, each read once and shared by every item of the call.
/// Every read is an existing worker method through the ordinary binding and access gates.
/// </summary>
internal sealed class PlcAnalysisSession
{
    private readonly Dictionary<string, Evidence<CrossReferenceReport>> _reports = new(StringComparer.Ordinal);
    private Evidence<ProjectTreeObservation>? _tree;

    public PlcAnalysisSession(OpennessWorkerClient client) => Client = client;

    public OpennessWorkerClient Client { get; }

    internal sealed record Evidence<T>(T? Value, StructuredOperationFailure? Failure, IReadOnlyList<string> Warnings) where T : class;

    public async Task<Evidence<ProjectTreeObservation>> TreeAsync()
    {
        if (_tree is not null) return _tree;
        var call = await Client.BrowseProjectTreeV3SnapshotAsync().ConfigureAwait(false);
        var result = call.WorkerResult;
        if (!result.Success)
            return _tree = new(null, new(result.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                $"The project tree (block inventory) could not be read: {result.Error}"), result.Warnings.ToArray());
        try
        {
            return _tree = new(ProjectTreeWorkerPayloadContract.Decode(result, null, null), null, result.Warnings.ToArray());
        }
        catch (ProjectTreeProtocolException exception)
        {
            return _tree = new(null, new(exception.Category, exception.Message), result.Warnings.ToArray());
        }
    }

    /// <summary>Reads the cross-references of one target with the default filter (ObjectsWithReferences) and no cap.</summary>
    public async Task<Evidence<CrossReferenceReport>> CrossReferencesAsync(CrossReferenceSelectorInfo selector)
    {
        var key = string.Join("\u001f", selector.Path.Select(segment => segment.NodeType + "\u001e" + segment.Name))
            + "\u001d" + selector.Member?.Kind + "\u001e" + selector.Member?.Name;
        if (_reports.TryGetValue(key, out var cached)) return cached;
        var display = Display(selector);
        var result = await Client.ReadCrossReferencesAsync(selector, CrossReferenceFilterNames.ObjectsWithReferences, maxResults: null)
            .ConfigureAwait(false);
        Evidence<CrossReferenceReport> evidence;
        if (!result.Success)
        {
            evidence = new(null, new(result.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                $"The cross-references of '{display}' could not be read: {result.Error}"), result.Warnings.ToArray());
        }
        else
        {
            try
            {
                evidence = new(CrossReferencePayloadContract.Decode(result), null, result.Warnings.ToArray());
            }
            catch (JsonException)
            {
                evidence = new(null, new(WorkerFailureCategories.ProtocolError,
                    $"The cross-reference payload of '{display}' did not match its declared result contract and was rejected."), result.Warnings.ToArray());
            }
        }

        return _reports[key] = evidence;
    }

    public static string Display(CrossReferenceSelectorInfo selector)
        => string.Join("/", selector.Path.Select(segment => segment.Name)) + (selector.Member is { } member ? $" [{member.Kind} {member.Name}]" : string.Empty);
}
