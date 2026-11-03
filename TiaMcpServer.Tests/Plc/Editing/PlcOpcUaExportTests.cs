using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Json;
using TiaMcpServer.Plc;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Plc.Editing
{
    /// <summary>export_opcua_xml: the OPC UA data of one PLC as XML (OpcUaExportProvider.Export(DeviceItem, FileInfo), bound at runtime).</summary>
    [Collection(RealWorkerProcessCollection.Name)]
    public sealed class PlcOpcUaExportTests
    {
        private static PlcOperationRequest Export(string path, bool? overwrite = null, string? plc = "Station_1", string id = "o") => new()
        {
            OperationId = id, Operation = PlcBlockEditMethods.ExportOpcUaXml, PlcName = plc, FilePath = path, Overwrite = overwrite,
        };

        private static string Failures(PlcGuardedWriteResponse response)
            => $"{response.Phase} {response.Error?.Message} {string.Join("; ", response.Batch?.Operations.Select(o => o.Failure?.Message) ?? Array.Empty<string?>())}";

        [Fact]
        public void Catalog_RequiresTheFileAndChecksThePath()
        {
            Assert.Contains("filePath", PlcOperationCatalog.ValidateWrite(new[] { new PlcOperationRequest { OperationId = "o", Operation = PlcBlockEditMethods.ExportOpcUaXml } }).Error);
            Assert.False(PlcOperationCatalog.ValidateWrite(new[] { Export(@"\\?\C:\opcua.xml") }).IsValid);
            Assert.False(PlcOperationCatalog.ValidateWrite(new[] { Export(@"relative\opcua.xml") }).IsValid);
            Assert.True(PlcOperationCatalog.ValidateWrite(new[] { Export(@"C:\Exports\PLC_1.xml") }).IsValid);
            Assert.True(PlcOperationCatalog.ValidateWrite(new[] { Export(@"\\server\share\PLC_1.xml", plc: null) }).IsValid);
            Assert.Contains(PlcBlockEditMethods.ExportOpcUaXml, PlcOperationCatalog.WriteOperationNames);
            Assert.Contains(PlcBlockEditMethods.ExportOpcUaXml, PlcBlockEditMethods.CallerFileWrites);
        }

        [Fact]
        public void Policy_MapsTheExportToPlcWriteAsAProjectMutation()
        {
            Assert.Equal("plc_write", TiaMcpServer.Contracts.Project.ProjectCapabilityCatalog.OperationFor(PlcBlockEditMethods.ExportOpcUaXml));
            Assert.Equal(TiaMcpServer.Contracts.Safety.OperationCapability.ProjectMutation,
                TiaMcpServer.Contracts.Safety.OperationPolicyCatalog.GetCapability(PlcBlockEditMethods.ExportOpcUaXml));
        }

        [Fact]
        public void Binding_FindsExactlyTheDocumentedExport()
        {
            Assert.True(PlcOpcUaExport.IsProvider(typeof(Siemens.Engineering.HW.Utilities.OpcUaExportProvider)));
            Assert.True(PlcOpcUaExport.IsProvider(typeof(OpcUaBindingDoubles.DerivedProvider)));
            Assert.False(PlcOpcUaExport.IsProvider(typeof(Siemens.Engineering.HW.Utilities.ModuleInformationProvider)));

            var (method, missing) = PlcOpcUaExport.BindExport(typeof(Siemens.Engineering.HW.Utilities.OpcUaExportProvider));
            Assert.Null(missing);
            Assert.Equal(typeof(FileInfo), method!.GetParameters()[1].ParameterType);

            var (none, reason) = PlcOpcUaExport.BindExport(typeof(OpcUaBindingDoubles.WrongSignature));
            Assert.Null(none);
            Assert.Contains("no public Export", reason);
        }

        [Fact]
        public async Task Export_WritesTheFileAndRefusesAnExistingOneWithoutOverwrite()
        {
            using var audit = new TempAuditDirectory();
            using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");

            var preview = await fixture.RunAsync(true, Export(@"C:\Exports\PLC_1.xml"));
            Assert.True(preview.Success, Failures(preview));
            Assert.DoesNotContain(preview.Guards, g => g.Id == PlcGuardDefinitions.ExportOverwritesFile);

            var response = await fixture.RunAsync(false, Export(@"C:\Exports\PLC_1.xml"));
            Assert.True(response.Success, Failures(response));
            var result = CanonicalJson.Deserialize<PlcOpcUaExportResultInfo>(response.Batch!.Operations[0].Result!.Value.GetRawText());
            Assert.Equal(("PLC_1", "Station_1", false, 64), (result.PlcName, result.DeviceName, result.Overwrite, result.FileSha256.Length));
            Assert.Equal("file", Assert.Single(response.Verification!.Operations).Check); // K50

            var again = await fixture.RunAsync(false, Export(@"C:\Exports\PLC_1.xml"));
            Assert.Equal("validation_error", again.Batch!.Operations[0].Failure!.Category);

            var replaced = await fixture.RunAsync(false, Export(@"C:\Exports\PLC_1.xml", overwrite: true));
            Assert.True(replaced.Success, Failures(replaced));
            Assert.True(CanonicalJson.Deserialize<PlcOpcUaExportResultInfo>(replaced.Batch!.Operations[0].Result!.Value.GetRawText()).Overwrite);
        }

        [Fact]
        public async Task Export_WithoutTheProviderFailsApiMemberMissing()
        {
            using var audit = new TempAuditDirectory();
            using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");

            var response = await fixture.RunAsync(false, Export(@"C:\NoProvider\PLC_1.xml"));

            Assert.False(response.Success);
            Assert.Equal("api_member_missing", response.Batch!.Operations[0].Failure!.Category);
        }
    }

    internal static class OpcUaBindingDoubles
    {
        public sealed class DerivedProvider : Siemens.Engineering.HW.Utilities.OpcUaExportProvider
        {
        }

        public sealed class WrongSignature
        {
            public void Export(string deviceItem, FileInfo file) { }
        }
    }
}

namespace Siemens.Engineering.HW.Utilities
{
    /// <summary>Stand-in for the documented provider: only its name and its Export(DeviceItem, FileInfo) matter.</summary>
    public class OpcUaExportProvider
    {
        public void Export(Siemens.Engineering.HW.DeviceItem plc, FileInfo exportFile) { }
    }
}
