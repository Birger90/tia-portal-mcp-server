using System.Reflection;
using System.Text.Json;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Plc;
using TiaMcpServer.Safety;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

/// <summary>
/// Every field a write operation declares must reach the worker, asserted by VALUE so renaming either
/// side of the catalog-to-worker boundary cannot make it pass vacuously.
/// </summary>
[Collection(RealWorkerProcessCollection.Name)]
public sealed class PlcWriteInvokerTests
{
    private const string XmlHash = "xml:sha256:3333333333333333333333333333333333333333333333333333333333333333";
    private const string Hash = "source:sha256:2222222222222222222222222222222222222222222222222222222222222222";

    private static readonly Dictionary<string, object> ValidatedFieldValues = new(StringComparer.Ordinal)
    {
        ["blockType"] = "FB",
        ["language"] = "SCL",
        ["obEventClass"] = "CyclicInterrupt",
        ["dataType"] = "Bool",
    };

    private static OpennessWorkerClient CreateClient(string? workerPath = null)
        => new(
            new ProjectSessionBinding(null),
            logger: null,
            workerExecutablePath: workerPath ?? FakeWorkerLocator.Locate(),
            accessPolicy: new OperationAccessPolicy(McpAccessMode.Full));

    private static object SentinelFor(PropertyInfo property, string fieldName, string operation)
    {
        if (fieldName == "format")
        {
            // Defaults are xml for blocks and source for types; use the opposite valid value.
            return operation == "update_block_logic" ? SourceFormatNames.Source : SourceFormatNames.Xml;
        }

        if (fieldName == "expectedContentHash")
        {
            // The hash text must not contain another sentinel: block updates use format=source here.
            return operation == "update_block_logic" ? XmlHash : Hash;
        }

        if (ValidatedFieldValues.TryGetValue(fieldName, out var known))
        {
            return known;
        }

        // List-valued fields carry one sentinel element; the element is what must reach the worker.
        if (property.PropertyType == typeof(IReadOnlyList<string>) || property.PropertyType == typeof(string[])) return new[] { $"__sentinel_{fieldName}__" };
        if (property.PropertyType == typeof(TiaMcpServer.PlcDownload.PlcDownloadPasswordMap))
            return new TiaMcpServer.PlcDownload.PlcDownloadPasswordMap(new[] { new PlcDownloadPasswordInfo { Type = $"__sentinel_{fieldName}_key__", Variable = $"__sentinel_{fieldName}__" } });
        if (property.PropertyType == typeof(List<TiaMcpServer.Contracts.Plc.Interface.PlcTextReplacementInfo>))
            return new List<TiaMcpServer.Contracts.Plc.Interface.PlcTextReplacementInfo> { new() { Find = $"__sentinel_{fieldName}__", Replace = $"__sentinel_{fieldName}_by__" } };
        if (property.PropertyType == typeof(List<PlcNameRenameInfo>))
            return new List<PlcNameRenameInfo> { new() { Name = $"__sentinel_{fieldName}__", NewName = $"__sentinel_{fieldName}_new__" } };
        if (property.PropertyType == typeof(PlcSettingValues))
            return new PlcSettingValues(new[] { new KeyValuePair<string, string>($"__sentinel_{fieldName}_key__", $"__sentinel_{fieldName}__") });
        if (property.PropertyType == typeof(IReadOnlyList<TiaMcpServer.PlcAlarms.PlcAlarmCellEditRequest>))
            return new[] { new TiaMcpServer.PlcAlarms.PlcAlarmCellEditRequest { Sheet = $"__sentinel_{fieldName}__" } };

        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (type == typeof(int)) return 4242;
        if (type == typeof(IReadOnlyList<string>)) return new[] { $"__sentinel_{fieldName}__" };
        return type == typeof(bool) ? true : $"__sentinel_{fieldName}__";
    }

    private static PropertyInfo PropertyFor(string fieldName)
        => typeof(PlcOperationRequest).GetProperty(char.ToUpperInvariant(fieldName[0]) + fieldName.Substring(1))
            ?? throw new InvalidOperationException($"No PlcOperationRequest property for '{fieldName}'.");

    private static (PlcOperationRequest Request, List<object> Expected) Build(string operation)
    {
        Assert.True(PlcOperationCatalog.TryGetWriteFields(operation, out var required, out var optional));
        var request = new PlcOperationRequest { OperationId = "item-1", Operation = operation };
        var expected = new List<object>();
        foreach (var field in required.Concat(optional))
        {
            var property = PropertyFor(field);
            var value = SentinelFor(property, field, operation);
            property.SetValue(request, value);
            expected.Add(value switch
            {
                string[] items => items[0],
                TiaMcpServer.PlcDownload.PlcDownloadPasswordMap map => map.Entries[0].Variable,
                PlcSettingValues settings => settings.Entries[0].Value,
                TiaMcpServer.PlcAlarms.PlcAlarmCellEditRequest[] edits => edits[0].Sheet!,
                List<PlcNameRenameInfo> renames => renames[0].Name,
                List<TiaMcpServer.Contracts.Plc.Interface.PlcTextReplacementInfo> pairs => pairs[0].Find,
                _ => value,
            });
        }

        return (request, expected);
    }

    private static string Render(object value) => value switch
    {
        bool b => b ? "true" : "false",
        _ => JsonSerializer.Serialize(value).Trim('"'),
    };

    private static int Count(string value, string needle)
    {
        var count = 0;
        for (var i = value.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = value.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    public static TheoryData<string> AllWriteOperations()
    {
        var data = new TheoryData<string>();
        // Interface edits have no worker method: the domain imports their planned document (see PlcBlockEditWriteTests).
        // The file exports have none either: they compose existing reads in the host (see PlcSourceFolderMcpTests).
        foreach (var name in PlcOperationCatalog.WriteOperationNames.Except(PlcBlockEditMethods.PlannedEdits).Except(PlcSourceFolderMethods.HostWrites))
        {
            data.Add(name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllWriteOperations))]
    public async Task WriteSendsTheBoundPath(string operation)
    {
        var (request, _) = Build(operation);
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(
            binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate(),
            accessPolicy: new OperationAccessPolicy(McpAccessMode.Full));
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, "echo");

        var result = await new PlcWriteDomain(client).MutateAsync(request);

        Assert.True(result.Success, result.Error);
        using var echoed = JsonDocument.Parse(result.Payload);
        Assert.Equal(Path.GetFullPath("echo"), echoed.RootElement.GetProperty("projectPath").GetString());
    }

    [Theory]
    [MemberData(nameof(AllWriteOperations))]
    public async Task ForwardsEveryFieldToItsWorkerMethod(string operation)
    {
        var (request, expected) = Build(operation);
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(
            binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate(),
            accessPolicy: new OperationAccessPolicy(McpAccessMode.Full));
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, "echo");

        var result = await PlcWorkerInvoker.InvokeWriteAsync(client, request);
        Assert.True(result.Success, result.Error);
        var baseline = await PlcWorkerInvoker.InvokeWriteAsync(
            client, new PlcOperationRequest { OperationId = "baseline", Operation = operation });
        Assert.True(baseline.Success, baseline.Error);

        foreach (var group in expected.Select(Render).GroupBy(v => v, StringComparer.Ordinal))
        {
            var actual = Count(result.Payload, group.Key) - Count(baseline.Payload, group.Key);
            Assert.True(
                actual == group.Count(),
                $"'{operation}' expected {group.Count()} occurrences of '{group.Key}' but saw {actual}. Echo: {result.Payload}");
        }
    }

    [Theory]
    [InlineData("update_block_logic")]
    [InlineData("update_type_content")]
    public async Task ForwardsExpectedContentHash(string operation)
    {
        var (request, _) = Build(operation);
        request.Format = operation == "update_block_logic" ? "source" : null;
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(
            binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate(),
            accessPolicy: new OperationAccessPolicy(McpAccessMode.Full));
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, "echo");

        var result = await PlcWorkerInvoker.InvokeWriteAsync(client, request);

        Assert.True(result.Success, result.Error);
        using var echoed = JsonDocument.Parse(result.Payload);
        Assert.Equal(request.ExpectedContentHash, echoed.RootElement.GetProperty("expectedContentHash").GetString());
        Assert.Equal("__sentinel_content__", echoed.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public async Task InvalidFormatIsValidationErrorWithNotSentOutcome()
    {
        using var client = CreateClient(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe"));

        var result = await PlcWorkerInvoker.InvokeWriteAsync(client, new PlcOperationRequest
        {
            OperationId = "invalid-format",
            Operation = "update_block_logic",
            BlockPath = "PLC/Blocks/Main",
            Content = "secret submitted content",
            ExpectedContentHash = Hash,
            Format = "not-a-format",
        });

        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.ValidationError, result.FailureCategory);
        Assert.Equal(WorkerDispatchState.NotSent, result.DispatchState);
        var outcome = result.BlockImportOutcome;
        Assert.NotNull(outcome);
        Assert.Equal("not_started", outcome.ImportStage);
        Assert.False(outcome.TargetMutationCommitted);
        Assert.Equal("unknown", outcome.TemporarySourceState);
    }

    [Fact]
    public async Task InvalidTypeFormatIsValidationErrorWithoutOutcome()
    {
        using var client = CreateClient(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe"));

        var result = await PlcWorkerInvoker.InvokeWriteAsync(client, new PlcOperationRequest
        {
            OperationId = "invalid-format",
            Operation = "update_type_content",
            TypePath = "PLC/Types/T",
            Content = "x",
            ExpectedContentHash = Hash,
            Format = "nope",
        });

        Assert.Equal(WorkerFailureCategories.ValidationError, result.FailureCategory);
        Assert.Null(result.BlockImportOutcome);
    }

    [Fact]
    public async Task UnsupportedOperationIsValidationError()
    {
        using var client = CreateClient(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe"));

        var result = await PlcWorkerInvoker.InvokeWriteAsync(
            client, new PlcOperationRequest { OperationId = "x", Operation = "get_block_content", BlockPath = "a/b" });

        Assert.Equal(WorkerFailureCategories.ValidationError, result.FailureCategory);
    }
}
