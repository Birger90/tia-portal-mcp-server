using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc.SimaticSd;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.FakeWorker.Plc;
using TiaMcpServer.Plc;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace Siemens.Engineering.SW
{
    /// <summary>Stand-in with the documented V21 member names (values are the test's, not Siemens').</summary>
    [Flags]
    public enum SWImportOptions { None = 0, IgnoreStructuralChanges = 1, IgnoreMissingReferencedObjects = 2, IgnoreUnitAttributes = 4 }
}

namespace TiaMcpServer.Tests.Plc
{
    /// <summary>Audit N7/N8: caller import options (shared binding, host shape rules, FakeWorker round trips). Not live-tested.</summary>
    [Collection(RealWorkerProcessCollection.Name)]
    public sealed class PlcImportOptionsTests
    {
        private enum Sequential { None = 0, Override = 1, SkipInactiveCultures = 2, ActivateInactiveCultures = 3 }

        [Flags]
        private enum Flagged { None = 0, Override = 1, SkipInactiveCultures = 2, ActivateInactiveCultures = 4 }

        private sealed class ImportTarget
        {
            public List<(FileInfo File, Siemens.Engineering.ImportOptions Options, Siemens.Engineering.SW.SWImportOptions Sw)> Calls { get; } = new();

            public IList<object> Import(FileInfo file, Siemens.Engineering.ImportOptions options) => throw new InvalidOperationException("two-argument overload");

            public IList<object> Import(FileInfo file, Siemens.Engineering.ImportOptions options, Siemens.Engineering.SW.SWImportOptions sw)
            {
                if (file.Name == "throw.xml") throw new ArgumentException("rejected by the stand-in");
                Calls.Add((file, options, sw));
                return new List<object> { "imported" };
            }
        }

        private sealed class NoOverload
        {
            public IList<object> Import(FileInfo file, Siemens.Engineering.ImportOptions options) => new List<object>();
        }

        [Fact]
        public void Combine_OrsMembersOfAFlagsEnum_AndRefusesToCombineASequentialOne()
        {
            Assert.Equal(Flagged.Override | Flagged.SkipInactiveCultures, PlcImportOptionNames.DocumentOptions(typeof(Flagged), "SkipInactiveCultures"));
            Assert.Equal(Flagged.Override, PlcImportOptionNames.DocumentOptions(typeof(Flagged), null));
            Assert.Equal(Sequential.Override, PlcImportOptionNames.DocumentOptions(typeof(Sequential), null));
            var refused = Assert.Throws<RuntimeBindingException>(() => PlcImportOptionNames.DocumentOptions(typeof(Sequential), "SkipInactiveCultures"));
            Assert.Equal(WorkerFailureCategories.UnsupportedCapability, refused.Category);
            Assert.Contains("nothing was imported", refused.Message);
            var missing = Assert.Throws<RuntimeBindingException>(() => PlcImportOptionNames.Combine(typeof(Flagged), new[] { "IgnoreStructuralChanges" }, "x"));
            Assert.Equal(WorkerFailureCategories.ApiMemberMissing, missing.Category);
        }

        [Fact]
        public void BindSwImport_BindsTheThreeArgumentOverloadWithOverride_AndFailsClosedWithoutIt()
        {
            var composition = new ImportTarget();
            var import = PlcImportOptionNames.BindSwImport(composition, new[] { "IgnoreStructuralChanges", "IgnoreUnitAttributes" });
            Assert.Empty(composition.Calls); // binding imports nothing
            import(new FileInfo("block.xml"));
            var call = Assert.Single(composition.Calls);
            Assert.Equal(Siemens.Engineering.ImportOptions.Override, call.Options);
            Assert.Equal(Siemens.Engineering.SW.SWImportOptions.IgnoreStructuralChanges | Siemens.Engineering.SW.SWImportOptions.IgnoreUnitAttributes, call.Sw);
            // The member's own exception is rethrown unwrapped.
            Assert.Throws<ArgumentException>(() => import(new FileInfo("throw.xml")));
            var missing = Assert.Throws<RuntimeBindingException>(() => PlcImportOptionNames.BindSwImport(new NoOverload(), new[] { "IgnoreStructuralChanges" }));
            Assert.Equal(WorkerFailureCategories.ApiMemberMissing, missing.Category);
        }

        [Theory]
        [InlineData("update_block_logic", null, new[] { "IgnoreStructuralChanges" }, null, null)]
        [InlineData("update_block_logic", "sd", null, "ActivateInactiveCultures", null)]
        [InlineData("update_type_content", "xml", new[] { "IgnoreMissingReferencedObjects" }, null, null)]
        [InlineData("update_type_content", null, new[] { "IgnoreMissingReferencedObjects" }, null, "format xml")]
        [InlineData("update_block_logic", "source", null, "SkipInactiveCultures", "SIMATIC SD")]
        [InlineData("update_block_logic", "sd", new[] { "IgnoreStructuralChanges" }, null, "format xml")]
        [InlineData("update_block_logic", null, new[] { "IgnoreStructuralChanges", "IgnoreStructuralChanges" }, null, "more than once")]
        [InlineData("update_block_logic", null, new[] { "Override" }, null, "is not one of")]
        [InlineData("update_block_logic", "sd", null, "Override", "documentImportOption must be")]
        [InlineData("update_type_content", "xml", null, "SkipInactiveCultures", "not valid for update_type_content")]
        public void Catalog_AcceptsOptionsOnlyForTheirFormat(string operation, string? format, string[]? sw, string? document, string? error)
        {
            var op = new PlcOperationRequest
            {
                OperationId = "o", Operation = operation, Format = format, Content = "<x/>",
                ExpectedContentHash = (format ?? (operation == "update_type_content" ? "source" : "xml")) + ":sha256:" + new string('0', 64),
                BlockPath = operation == "update_block_logic" ? "PLC_1/Main" : null, TypePath = operation == "update_type_content" ? "PLC_1/Types/T" : null,
                SwImportOptions = sw, DocumentImportOption = document,
            };
            var validation = PlcOperationCatalog.ValidateWrite(new[] { op });
            if (error is null) Assert.True(validation.IsValid, validation.Error);
            else Assert.Contains(error, validation.Error);
        }

        [Fact]
        public void Catalog_RefusesTheOptionsOnOtherOperations()
        {
            var op = new PlcOperationRequest { OperationId = "o", Operation = "delete_block", BlockPath = "PLC_1/Main", SwImportOptions = new[] { "IgnoreStructuralChanges" } };
            Assert.Contains("'swImportOptions' is not valid for delete_block", PlcOperationCatalog.ValidateWrite(new[] { op }).Error);
        }

        [Fact]
        public async Task UpdateBlockLogicXml_IsAbortedOnAStructuralChangeUntilIgnoreStructuralChangesIsPassed()
        {
            using var audit = new TempAuditDirectory();
            using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");
            const string block = "Station_1/Main";
            var read = new PlcOperationRequest { OperationId = "r", Operation = "get_block_content", BlockPath = block, Format = "xml" };
            var current = PlcPayloadContract.Project(read, await PlcWorkerInvoker.InvokeReadAsync(fixture.Client, read)).Result!.Value;
            var content = current.GetProperty("content").GetString()!.Replace("</Document>", "<!-- fake:structural-change --></Document>");
            if (!content.Contains("fake:structural-change", StringComparison.Ordinal)) content += "\n<!-- fake:structural-change -->";
            var hash = current.GetProperty("contentHash").GetString()!;
            PlcOperationRequest Update(string id, string[]? sw) => new()
            {
                OperationId = id, Operation = "update_block_logic", BlockPath = block, Format = "xml", Content = content, ExpectedContentHash = hash, SwImportOptions = sw,
            };

            var refused = await fixture.RunAsync(false, Update("plain", null));
            Assert.False(refused.Success);
            Assert.Contains("IgnoreStructuralChanges", refused.Batch!.Operations[0].Failure!.Message);

            var applied = await fixture.RunAsync(false, Update("ignore", new[] { "IgnoreStructuralChanges", "IgnoreMissingReferencedObjects" }));
            Assert.True(applied.Success, applied.Error?.Message ?? applied.Batch?.Operations[0].Failure?.Message);
        }

        [Fact]
        public async Task CreateBlockFromDocument_WithAnInactiveCultureNeedsACultureOption()
        {
            using var audit = new TempAuditDirectory();
            using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");
            var bundle = SimaticSdFixtures.CookbookFbLadV3.Bundle.Replace("MCPVerify_FB_LAD_v3", "FB_Cultures")
                .Replace("END_FUNCTION_BLOCK", "// fake:inactive-culture\nEND_FUNCTION_BLOCK");
            PlcOperationRequest Create(string id, string? option) => new()
            {
                OperationId = id, Operation = SimaticSdMethods.CreateBlockFromDocument, TargetGroupPath = "Station_1/Blocks", Content = bundle, DocumentImportOption = option,
            };

            var refused = await fixture.RunAsync(false, Create("plain", null));
            Assert.False(refused.Success);
            Assert.Contains("culture", refused.Batch!.Operations[0].Failure!.Message);

            var created = await fixture.RunAsync(false, Create("skip", PlcImportOptionNames.SkipInactiveCultures));
            Assert.True(created.Success, created.Error?.Message ?? created.Batch?.Operations[0].Failure?.Message);
        }
    }
}
