using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc.Loadable;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Plc;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Plc.Loadable;

/// <summary>
/// Issue #26: plc_write generate_loadable. The Siemens-free overload binding against stand-in providers whose types carry the
/// documented full names, the caller-file rules, the catalog, policy and decoding, and the plc-write-roundtrip FakeWorker end
/// to end. Not live-tested: where the provider hangs, what the call writes and which extension it wants are decided only live.
/// </summary>
[Collection(RealWorkerProcessCollection.Name)]
public sealed class PlcLoadableTests
{
    [Fact]
    public void Bind_FindsTheOverloadForTheElementType_AndTheTargetMember()
    {
        var provider = new Siemens.Engineering.SW.Loader.LoadableProvider();
        var blocks = PlcLoadableRun.Bind(provider, PlcLoadableNames.BlockType, "PlcSim");
        Assert.Equal(typeof(Siemens.Engineering.SW.Blocks.PlcBlock), blocks.ElementType);
        Assert.Equal("PlcSim", blocks.Target.ToString());
        Assert.Contains("GenerateLoadable(FileInfo", blocks.Signature);
        Assert.Equal(typeof(Siemens.Engineering.SW.Units.PlcUnit), PlcLoadableRun.Bind(provider, PlcLoadableNames.UnitType, "Plc").ElementType);

        var member = Assert.Throws<RuntimeBindingException>(() => PlcLoadableRun.Bind(provider, PlcLoadableNames.BlockType, "Device"));
        Assert.Equal(WorkerFailureCategories.ApiMemberMissing, member.Category);
        var overload = Assert.Throws<RuntimeBindingException>(() => PlcLoadableRun.Bind(new object(), PlcLoadableNames.BlockType, "Plc"));
        Assert.Equal(WorkerFailureCategories.ApiMemberMissing, overload.Category);
        Assert.Contains("Nothing was written", overload.Message);
    }

    [Fact]
    public void Generate_PlacesTheFile_RefusesOverwriteUnasked_AndFailsWhenNothingIsWritten()
    {
        var directory = Directory.CreateTempSubdirectory("tia-mcp-loadable-").FullName;
        try
        {
            var provider = new Siemens.Engineering.SW.Loader.LoadableProvider();
            var bound = PlcLoadableRun.Bind(provider, PlcLoadableNames.BlockType, "Plc");
            var path = Path.Combine(directory, "Main.loadable");
            var objects = new object[] { new Siemens.Engineering.SW.Blocks.PlcBlock { Name = "Main" } };
            var result = PlcLoadableRun.Generate(provider, bound, objects, path, overwrite: false, DateTime.UtcNow, new PlcLoadableResultInfo());
            Assert.True(File.Exists(path));
            Assert.Equal(TiaMcpServer.Contracts.PlcAlarms.PlcAlarmFiles.Hash(path).Sha256, result.Sha256);
            Assert.False(result.Overwritten);
            Assert.Single(Directory.GetFiles(directory));

            var refused = Assert.Throws<RuntimeBindingException>(() => PlcLoadableRun.Generate(provider, bound, objects, path, overwrite: false, DateTime.UtcNow, new PlcLoadableResultInfo()));
            Assert.Equal(WorkerFailureCategories.ValidationError, refused.Category);
            Assert.True(PlcLoadableRun.Generate(provider, bound, objects, path, overwrite: true, DateTime.UtcNow, new PlcLoadableResultInfo()).Overwritten);

            var silent = new SilentProvider();
            var none = Assert.Throws<RuntimeBindingException>(() => PlcLoadableRun.Generate(silent, PlcLoadableRun.Bind(silent, PlcLoadableNames.BlockType, "Plc"),
                objects, Path.Combine(directory, "none.bin"), false, DateTime.UtcNow, new PlcLoadableResultInfo()));
            Assert.Equal(WorkerFailureCategories.PostconditionFailed, none.Category);
            Assert.False(File.Exists(Path.Combine(directory, "none.bin")));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class SilentProvider
    {
        public void GenerateLoadable(FileInfo path, IEnumerable<Siemens.Engineering.SW.Blocks.PlcBlock> blocks, Siemens.Engineering.SW.Loader.TargetOption targetOption)
        {
        }
    }

    [Fact]
    public void Catalog_NeedsTargetFilePathAndExactlyOneObjectList()
    {
        PlcOperationRequest Op(IReadOnlyList<string>? blocks, IReadOnlyList<string>? units, string? target = "Plc")
            => new() { OperationId = "l", Operation = PlcLoadableMethods.GenerateLoadable, FilePath = "/tmp/out.bin", Target = target, BlockPaths = blocks, UnitNames = units };

        Assert.True(PlcOperationCatalog.ValidateWrite(new[] { Op(new[] { "PLC_1/Main" }, null) }).IsValid, PlcOperationCatalog.ValidateWrite(new[] { Op(new[] { "PLC_1/Main" }, null) }).Error);
        Assert.True(PlcOperationCatalog.ValidateWrite(new[] { Op(null, new[] { "Unit_A" }, "PlcSim") }).IsValid);
        Assert.False(PlcOperationCatalog.ValidateWrite(new[] { Op(null, null) }).IsValid);
        Assert.False(PlcOperationCatalog.ValidateWrite(new[] { Op(new[] { "PLC_1/Main" }, new[] { "Unit_A" }) }).IsValid);
        Assert.False(PlcOperationCatalog.ValidateWrite(new[] { Op(new[] { "PLC_1/Main" }, null, "None") }).IsValid);
        Assert.False(PlcOperationCatalog.ValidateWrite(new[] { Op(new[] { "PLC_1/Main" }, null, null) }).IsValid);
        Assert.False(PlcOperationCatalog.ValidateWrite(new[] { Op(new[] { "PLC_1/Main", "plc_1/main" }, null) }).IsValid);
        Assert.False(PlcOperationCatalog.ValidateWrite(new[] { Op(new[] { "PLC_1/Main", "PLC_2/Main" }, null) }).IsValid);
        Assert.False(PlcOperationCatalog.ValidateRead(new[] { Op(new[] { "PLC_1/Main" }, null) }).IsValid);
    }

    [Fact]
    public void Policy_IsAProjectMutation_GatedAsPlcWrite()
    {
        Assert.Equal(OperationCapability.ProjectMutation, OperationPolicyCatalog.GetCapability(PlcLoadableMethods.GenerateLoadable));
        Assert.False(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, PlcLoadableMethods.GenerateLoadable));
        Assert.Equal("plc_write", ProjectCapabilityCatalog.OperationFor(PlcLoadableMethods.GenerateLoadable));
    }

    [Fact]
    public void Decode_RequiresTheRequestedObjectsAndAHash()
    {
        var item = new PlcOperationRequest { OperationId = "l", Operation = PlcLoadableMethods.GenerateLoadable, FilePath = "/tmp/out.bin", Target = "Plc", BlockPaths = new[] { "PLC_1/Main" } };
        var result = new PlcLoadableResultInfo
        {
            PlcName = "PLC_1", Overload = PlcLoadableNames.OverloadBlocks, Objects = { "Main" }, Target = "Plc", Signature = "Void GenerateLoadable(...)",
            FilePath = "/tmp/out.bin", Bytes = 3, Sha256 = new string('a', 64),
        };
        var element = PlcLoadable.DecodeWrite(item, WorkerJson.SerializePayload(result));
        var check = PlcLoadable.Verify(item, null, new Dictionary<string, JsonElement> { ["l"] = element });
        Assert.True(check.Success);
        Assert.Equal(PlcVerificationPolicy.FileCheck, check.Check);

        result.Overwritten = true;
        Assert.ThrowsAny<JsonException>(() => PlcLoadable.DecodeWrite(item, WorkerJson.SerializePayload(result)));
        result.Overwritten = false;
        result.Overload = PlcLoadableNames.OverloadUnits;
        Assert.ThrowsAny<JsonException>(() => PlcLoadable.DecodeWrite(item, WorkerJson.SerializePayload(result)));
    }

    [Fact]
    public async Task FakeWorker_GeneratesForBlocksAndUnits_AndAPlcWithoutTheProviderIsUnsupported()
    {
        var directory = Directory.CreateTempSubdirectory("tia-mcp-loadable-mcp-").FullName;
        try
        {
            using var audit = new TempAuditDirectory();
            using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip", McpAccessMode.ReadWrite);
            var blocks = new PlcOperationRequest
            {
                OperationId = "b", Operation = PlcLoadableMethods.GenerateLoadable, PlcName = "Station_1", BlockPaths = new[] { "PLC_1/Main" },
                Target = "PlcSim", FilePath = Path.Combine(directory, "main.bin"),
            };
            var units = new PlcOperationRequest
            {
                OperationId = "u", Operation = PlcLoadableMethods.GenerateLoadable, PlcName = "Station_1", UnitNames = new[] { "Unit_A" },
                Target = "Plc", FilePath = Path.Combine(directory, "unit.bin"),
            };

            var preview = await fixture.RunAsync(true, blocks);
            Assert.Equal("preview", preview.Phase);
            Assert.Equal(PlcLoadable.Kind, Assert.Single(preview.Effects).Effect!.Target.Kind);
            Assert.False(File.Exists(blocks.FilePath));

            var applied = await fixture.RunAsync(false, blocks, units);
            Assert.True(applied.Success, JsonSerializer.Serialize(applied.Error));
            Assert.All(applied.Batch!.Operations, operation => Assert.Equal("succeeded", operation.Status));
            Assert.Equal(TiaMcpServer.Contracts.PlcAlarms.PlcAlarmFiles.Hash(blocks.FilePath).Sha256,
                applied.Batch.Operations[0].Result!.Value.GetProperty("sha256").GetString());
            Assert.All(applied.Verification!.Operations, check => Assert.Equal((PlcVerificationPolicy.FileCheck, true), (check.Check, check.Success)));

            var other = await fixture.RunAsync(false, new PlcOperationRequest
            {
                OperationId = "o", Operation = PlcLoadableMethods.GenerateLoadable, PlcName = "PLC_2", BlockPaths = new[] { "PLC_2/Main" },
                Target = "Plc", FilePath = Path.Combine(directory, "other.bin"),
            });
            Assert.Equal(WorkerFailureCategories.UnsupportedCapability, other.Batch!.Operations.Single().Failure!.Category);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
