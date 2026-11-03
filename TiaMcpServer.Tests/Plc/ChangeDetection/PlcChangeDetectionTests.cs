using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc.ChangeDetection;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Plc;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Plc.ChangeDetection;

/// <summary>
/// Issue #6: plc_read get_plc_checksums and get_fingerprints. The Siemens-free run against stand-in providers (members as
/// Siemens.Engineering.Step7.xml documents them), the host catalog, policy and decoding, and the plc-change-detection
/// FakeWorker end to end. Not live-tested: which object lists the providers and the value types are decided only live.
/// </summary>
[Collection("Mcp protocol serial")]
public sealed class PlcChangeDetectionTests
{
    private sealed class ChecksumStandIn
    {
        public byte[] Software { get; set; } = { 0x0A, 0xFF };

        public string? TextLists { get; set; }
    }

    private sealed class ThrowingChecksums
    {
        public string Software => throw new InvalidOperationException("not compiled");

        public string TextLists => throw new FormatException("fatal");
    }

    private sealed class FingerprintStandIn(string id, object value)
    {
        public string Id { get; } = id;

        public object Value { get; } = value;
    }

    private sealed class ProviderStandIn(IList<FingerprintStandIn>? fingerprints)
    {
        public IList<FingerprintStandIn>? GetFingerprints() => fingerprints;
    }

    private sealed class NoMethodProvider;

    private static readonly Func<Exception, bool> Recoverable = exception => exception is InvalidOperationException;

    private static (List<Type> Offered, Func<Type, object?> Retrieve) Offer(object service, bool retrievable = true)
        => (new List<Type> { service.GetType() }, _ => retrievable ? service : null);

    [Fact]
    public void Checksums_AreProjectedAsText_WithTheirRuntimeType()
    {
        var provider = new Siemens.Engineering.SW.PlcChecksumProvider { Software = new byte[] { 0x0A, 0xFF }, TextLists = null };
        var (offered, retrieve) = Offer(provider);
        var entry = PlcChangeDetectionRun.ReadChecksums("PLC_1", "Station_1", offered, retrieve, Recoverable);

        Assert.True(entry.Available);
        Assert.Null(entry.Unavailable);
        Assert.Equal(RuntimeValueStates.Value, entry.Software!.State);
        Assert.Equal("0AFF", entry.Software.Value);
        Assert.Equal(typeof(byte[]).FullName, entry.Software.RuntimeType);
        Assert.Equal(RuntimeValueStates.Null, entry.TextLists!.State);
        provider.TextLists = string.Empty;
        Assert.Equal(RuntimeValueStates.Empty, PlcChangeDetectionRun.ReadChecksums("PLC_1", "Station_1", offered, retrieve, Recoverable).TextLists!.State);
        var missing = RuntimeServiceValues.ReadProperty(provider, "Other", Recoverable);
        Assert.Equal(RuntimeValueStates.Missing, missing.State);
        Assert.NotNull(missing.Error);

        var notReturned = PlcChangeDetectionRun.ReadChecksums("PLC_1", "Station_1", offered, _ => null, Recoverable);
        Assert.False(notReturned.Available);
        Assert.Contains("GetService returned null", notReturned.Unavailable);
        Assert.Null(notReturned.Software);
        var notListed = PlcChangeDetectionRun.ReadChecksums("PLC_1", "Station_1", new List<Type>(), retrieve, Recoverable);
        Assert.Contains("does not list 'Siemens.Engineering.SW.PlcChecksumProvider'", notListed.Unavailable);
    }

    [Fact]
    public void Fingerprints_AreReadThroughTheListedProvider()
    {
        var provider = new Siemens.Engineering.SW.FingerprintProvider(new List<object> { new FingerprintStandIn("Code", 42L) });
        var (offered, retrieve) = Offer(provider);
        var read = PlcChangeDetectionRun.ReadFingerprints("block", "PLC_1/Main", "PLC_1", offered, retrieve, Recoverable);
        var print = Assert.Single(read.Fingerprints);
        Assert.Equal("Code", print.Id.Value);
        Assert.Equal("42", print.Value.Value);
        Assert.Equal(typeof(long).FullName, print.Value.RuntimeType);

        var none = Assert.Throws<RuntimeBindingException>(() => PlcChangeDetectionRun.ReadFingerprints("block", "PLC_1/Main", "PLC_1",
            Offer(new Siemens.Engineering.SW.FingerprintProvider(null)).Offered, _ => new Siemens.Engineering.SW.FingerprintProvider(null), Recoverable));
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, none.Category);
        var unreturned = Assert.Throws<RuntimeBindingException>(() => PlcChangeDetectionRun.ReadFingerprints("type", "PLC_1/Types/T", "PLC_1", offered, _ => null, Recoverable));
        Assert.Equal(WorkerFailureCategories.UnsupportedCapability, unreturned.Category);
    }

    [Fact]
    public void ReadProperty_ReportsARecoverableFailure_AndRethrowsAnyOther()
    {
        var provider = new ThrowingChecksums();
        var unreadable = RuntimeServiceValues.ReadProperty(provider, "Software", Recoverable);
        Assert.Equal(RuntimeValueStates.Unreadable, unreadable.State);
        Assert.Contains("not compiled", unreadable.Error);
        Assert.Throws<FormatException>(() => RuntimeServiceValues.ReadProperty(provider, "TextLists", Recoverable));
    }

    [Fact]
    public void Find_TellsNotListed_FromListedButNotReturned()
    {
        var provider = new ChecksumStandIn();
        Assert.Equal((null, null), RuntimeServiceValues.Find(new List<Type>(), _ => provider, "X.Y"));
        var (service, unavailable) = RuntimeServiceValues.Find(new List<Type> { typeof(ChecksumStandIn) }, _ => null, typeof(ChecksumStandIn).FullName!);
        Assert.Null(service);
        Assert.Contains("GetService returned null", unavailable);
        Assert.Same(provider, RuntimeServiceValues.Find(new List<Type> { typeof(ChecksumStandIn) }, _ => provider, typeof(ChecksumStandIn).FullName!).Service);
    }

    [Fact]
    public void Fingerprints_FailClosed_WhenTheProviderOrItsMethodIsMissing()
    {
        var notListed = Assert.Throws<RuntimeBindingException>(() => PlcChangeDetectionRun.ReadFingerprints("block", "PLC_1/Main", "PLC_1",
            new List<Type>(), _ => null, Recoverable));
        Assert.Equal(WorkerFailureCategories.UnsupportedCapability, notListed.Category);
        Assert.Contains("No fingerprint was read.", notListed.Message);
    }

    [Fact]
    public void FingerprintElements_AreReadByName_AndANullReturnFails()
    {
        // The run matches the provider by its Siemens full name only, so these checks use the shared helpers it is built on.
        var provider = new ProviderStandIn(new List<FingerprintStandIn> { new("Code", 42L) });
        var returned = RuntimeServiceValues.Invoke(provider, "GetFingerprints", Type.EmptyTypes, Array.Empty<object?>(), "doc");
        var element = Assert.Single(RuntimeServiceValues.Elements(returned, "doc"));
        Assert.Equal("Code", RuntimeServiceValues.ReadProperty(element!, "Id", Recoverable).Value);
        var value = RuntimeServiceValues.ReadProperty(element!, "Value", Recoverable);
        Assert.Equal("42", value.Value);
        Assert.Equal(typeof(long).FullName, value.RuntimeType);

        var none = Assert.Throws<RuntimeBindingException>(() => RuntimeServiceValues.Elements(
            RuntimeServiceValues.Invoke(new ProviderStandIn(null), "GetFingerprints", Type.EmptyTypes, Array.Empty<object?>(), "doc"), "doc"));
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, none.Category);
        var missing = Assert.Throws<RuntimeBindingException>(() => RuntimeServiceValues.Invoke(new NoMethodProvider(), "GetFingerprints", Type.EmptyTypes, Array.Empty<object?>(), "doc"));
        Assert.Equal(WorkerFailureCategories.ApiMemberMissing, missing.Category);
    }

    [Fact]
    public void Catalog_GetFingerprintsNeedsExactlyOneTarget_AndChecksumsTakeAnOptionalPlc()
    {
        PlcOperationRequest Op(string operation, string? block = null, string? type = null, string? plc = null)
            => new() { OperationId = "r", Operation = operation, BlockPath = block, TypePath = type, PlcName = plc };

        Assert.True(PlcOperationCatalog.ValidateRead(new[] { Op(PlcChangeDetectionMethods.GetPlcChecksums) }).IsValid);
        Assert.True(PlcOperationCatalog.ValidateRead(new[] { Op(PlcChangeDetectionMethods.GetPlcChecksums, plc: "PLC_1") }).IsValid);
        Assert.False(PlcOperationCatalog.ValidateRead(new[] { Op(PlcChangeDetectionMethods.GetPlcChecksums, block: "PLC_1/Main") }).IsValid);
        Assert.True(PlcOperationCatalog.ValidateRead(new[] { Op(PlcChangeDetectionMethods.GetFingerprints, block: "PLC_1/Main") }).IsValid);
        Assert.True(PlcOperationCatalog.ValidateRead(new[] { Op(PlcChangeDetectionMethods.GetFingerprints, type: "PLC_1/Types/UDT_1") }).IsValid);
        var neither = PlcOperationCatalog.ValidateRead(new[] { Op(PlcChangeDetectionMethods.GetFingerprints) });
        Assert.False(neither.IsValid);
        Assert.Contains("exactly one of blockPath", neither.Error);
        Assert.False(PlcOperationCatalog.ValidateRead(new[] { Op(PlcChangeDetectionMethods.GetFingerprints, "PLC_1/Main", "PLC_1/Types/UDT_1") }).IsValid);
        Assert.False(PlcOperationCatalog.ValidateWrite(new[] { Op(PlcChangeDetectionMethods.GetPlcChecksums) }).IsValid);
        Assert.Contains(PlcChangeDetectionMethods.GetPlcChecksums, PlcOperationCatalog.ReadOperationNames);
        Assert.Contains(PlcChangeDetectionMethods.GetFingerprints, PlcOperationCatalog.ReadOperationNames);
    }

    [Fact]
    public void Policy_ObservesInEveryMode_AndGatesAsPlcRead()
    {
        foreach (var method in PlcChangeDetectionMethods.Read)
        {
            Assert.Equal(OperationCapability.Observe, OperationPolicyCatalog.GetCapability(method));
            Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, method));
            Assert.Equal("plc_read", ProjectCapabilityCatalog.OperationFor(method));
            Assert.True(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.LocalSession, method));
        }
    }

    [Fact]
    public void Decode_RejectsAContradictingPayload()
    {
        var op = new PlcOperationRequest { OperationId = "r", Operation = PlcChangeDetectionMethods.GetPlcChecksums, PlcName = "PLC_1" };
        var good = new PlcChecksumReadInfo
        {
            Plcs =
            {
                new PlcChecksumPlcInfo
                {
                    PlcName = "PLC_1", DeviceName = "Station_1", Available = true,
                    Software = new RuntimeValueInfo { State = RuntimeValueStates.Value, Value = "AB", RuntimeType = "System.String" },
                    TextLists = new RuntimeValueInfo { State = RuntimeValueStates.Empty, Value = string.Empty, RuntimeType = "System.String" },
                },
            },
        };
        var element = PlcChangeDetectionPayloadContract.DecodeRead(op, WorkerJson.SerializePayload(good));
        Assert.Equal("AB", element.GetProperty("plcs")[0].GetProperty("software").GetProperty("value").GetString());

        good.Plcs[0].PlcName = "PLC_9";
        good.Plcs[0].DeviceName = "Station_9";
        Assert.ThrowsAny<JsonException>(() => PlcChangeDetectionPayloadContract.DecodeRead(op, WorkerJson.SerializePayload(good)));
        good.Plcs[0].PlcName = "PLC_1";
        good.Plcs[0].Software!.State = RuntimeValueStates.Empty;
        Assert.ThrowsAny<JsonException>(() => PlcChangeDetectionPayloadContract.DecodeRead(op, WorkerJson.SerializePayload(good)));
        good.Plcs[0].Software!.State = RuntimeValueStates.Value;
        good.Plcs[0].Available = false;
        Assert.ThrowsAny<JsonException>(() => PlcChangeDetectionPayloadContract.DecodeRead(op, WorkerJson.SerializePayload(good)));

        var fingerprints = new PlcOperationRequest { OperationId = "f", Operation = PlcChangeDetectionMethods.GetFingerprints, BlockPath = "PLC_1/Main" };
        var typeResult = new PlcFingerprintReadInfo { ObjectKind = PlcChangeDetectionNames.ObjectKindType, Path = "PLC_1/Main", PlcName = "PLC_1" };
        Assert.ThrowsAny<JsonException>(() => PlcChangeDetectionPayloadContract.DecodeRead(fingerprints, WorkerJson.SerializePayload(typeResult)));
    }

    private static async Task<(McpProtocolTestHarness Harness, TempAuditDirectory Audit, FakeWorkerRequestLog Log, FakeWorkerUiOpenProject Ui)> StartAsync()
    {
        var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        var log = new FakeWorkerRequestLog(audit.Path);
        var ui = new FakeWorkerUiOpenProject("plc-change-detection");
        var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadOnly, audit.Path, "plc-change-detection");
        return (harness, audit, log, ui);
    }

    [Fact]
    public async Task Mcp_ReadsChecksumsOfEveryPlc_AndFingerprints_InReadOnly()
    {
        var (harness, audit, log, ui) = await StartAsync();
        try
        {
            var raw = await harness.Client.CallToolAsync("plc_read", new Dictionary<string, object?>
            {
                ["operations"] = new object[]
                {
                    new { operationId = "c", operation = "get_plc_checksums" },
                    new { operationId = "f", operation = "get_fingerprints", blockPath = "PLC_1/Main" },
                    new { operationId = "t", operation = "get_fingerprints", typePath = "PLC_1/Types/UDT_1" },
                },
            });
            Assert.Empty(StructuredContractInspector.FindViolations(raw));
            var document = raw.StructuredContent!.Value;
            var items = document.GetProperty("batch").GetProperty("operations");

            Assert.Equal("succeeded", items[0].GetProperty("status").GetString());
            var plcs = items[0].GetProperty("result").GetProperty("plcs");
            Assert.Equal(2, plcs.GetArrayLength());
            Assert.True(plcs[0].GetProperty("available").GetBoolean());
            Assert.Equal("9F3C01AB", plcs[0].GetProperty("software").GetProperty("value").GetString());
            Assert.Equal("empty", plcs[0].GetProperty("textLists").GetProperty("state").GetString());
            Assert.False(plcs[1].GetProperty("available").GetBoolean());
            Assert.Equal(JsonValueKind.Null, plcs[1].GetProperty("software").ValueKind);
            Assert.Contains("does not list", plcs[1].GetProperty("unavailable").GetString());
            Assert.Contains("change-detection", items[0].GetProperty("result").GetProperty("note").GetString());

            Assert.Equal("succeeded", items[1].GetProperty("status").GetString());
            var prints = items[1].GetProperty("result").GetProperty("fingerprints");
            Assert.Equal(2, prints.GetArrayLength());
            Assert.Equal("Code", prints[0].GetProperty("id").GetProperty("value").GetString());
            Assert.Equal("7d1e0a", prints[0].GetProperty("value").GetProperty("value").GetString());

            Assert.Equal("failed", items[2].GetProperty("status").GetString());
            Assert.Equal("unsupported_capability", items[2].GetProperty("failure").GetProperty("category").GetString());
            Assert.Contains(PlcChangeDetectionMethods.GetPlcChecksums, log.Methods());
        }
        finally
        {
            await harness.DisposeAsync();
            ui.Dispose();
            log.Dispose();
            audit.Dispose();
        }
    }
}
