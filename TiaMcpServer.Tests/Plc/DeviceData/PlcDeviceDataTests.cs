using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc.DeviceData;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Plc;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Plc.DeviceData;

/// <summary>
/// Issue #16: download_to_folder and the online master secret. Catalog, policy, guards, decoding and verification, and the
/// plc-write-roundtrip FakeWorker end to end (an in-memory model; no Siemens evidence). Not live-tested.
/// </summary>
[Collection(RealWorkerProcessCollection.Name)]
public sealed class PlcDeviceDataTests
{
    [Fact]
    public void Policy_FolderMutates_OnlineIsOnlineControl_AndAllMapToPlcWrite()
    {
        Assert.Equal(OperationCapability.ProjectMutation, OperationPolicyCatalog.GetCapability(PlcDeviceDataMethods.DownloadToFolder));
        foreach (var method in PlcDeviceDataMethods.Online)
        {
            Assert.Equal(OperationCapability.OnlineControl, OperationPolicyCatalog.GetCapability(method));
            Assert.False(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, method));
        }

        foreach (var method in PlcDeviceDataMethods.Write)
        {
            Assert.Equal("plc_write", ProjectCapabilityCatalog.OperationFor(method));
            Assert.Contains(method, PlcOperationCatalog.WriteOperationNames);
        }
    }

    public static TheoryData<PlcOperationRequest, string> Invalid => new()
    {
        { new() { OperationId = "a", Operation = PlcDeviceDataMethods.DownloadToFolder }, "directoryPath" },
        { new() { OperationId = "a", Operation = PlcDeviceDataMethods.DownloadToFolder, DirectoryPath = "relative" }, "absolute" },
        { new() { OperationId = "a", Operation = PlcDeviceDataMethods.SetMasterSecretOnline }, "passwordVariable" },
        { new() { OperationId = "a", Operation = PlcDeviceDataMethods.SetMasterSecretOnline, PasswordVariable = TiaMcpServer.PlcDownload.PlcDownloadPasswordMap.RedactedVariable }, "never the secret itself" },
        { new() { OperationId = "a", Operation = PlcDeviceDataMethods.ResetMasterSecretOnline, DirectoryPath = @"C:\Download" }, "directoryPath" },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Validation_RefusesMisshapedItems(PlcOperationRequest item, string fragment)
    {
        var result = PlcOperationCatalog.ValidateWrite(new[] { item });
        Assert.False(result.IsValid);
        Assert.Contains(fragment, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_OnlineItemsFireTheAcknowledgeGuard_TheFolderDownloadOnlyInfo()
    {
        var plc = new TiaMcpServer.Contracts.Plc.PlcTagInventoryPlcInfo { PlcName = "PLC_1", DeviceName = "Station_1" };
        var reset = PlcDeviceData.Resolve(new PlcOperationRequest { OperationId = "r", Operation = PlcDeviceDataMethods.ResetMasterSecretOnline }, plc);
        Assert.Contains(reset.Guards, g => g.Id == PlcGuardDefinitions.MasterSecretOnline);
        var folder = PlcDeviceData.Resolve(new PlcOperationRequest { OperationId = "f", Operation = PlcDeviceDataMethods.DownloadToFolder, DirectoryPath = @"C:\Download" }, plc);
        Assert.DoesNotContain(folder.Guards, g => g.Id == PlcGuardDefinitions.MasterSecretOnline);
        Assert.Contains(PlcGuardDefinitions.Definitions, d => d.Id == PlcGuardDefinitions.MasterSecretOnline && d.Severity == TiaMcpServer.Safety.Pipeline.WriteGuardSeverities.Acknowledge);
    }

    [Fact]
    public void Decode_AndVerify_TheOnlineSecretIsNeverVerified()
    {
        var item = new PlcOperationRequest { OperationId = "s", Operation = PlcDeviceDataMethods.SetMasterSecretOnline, PasswordVariable = "TIA_SECRET" };
        var result = new PlcDeviceDataResultInfo
        {
            Operation = item.Operation, PlcName = "PLC_1", Call = "OnlineProvider.SetPlcMasterSecret(SecureString)", OnlineState = "Online",
            PasswordSource = "TIA_SECRET", Evidence = "notReadBack", Note = "no read-back",
        };
        var element = PlcDeviceData.DecodeWrite(item, WorkerJson.SerializePayload(result));
        Assert.False(PlcDeviceData.Verify(item, null, new Dictionary<string, JsonElement> { ["s"] = element }).Success);
        result.PasswordSource = "OTHER";
        Assert.ThrowsAny<JsonException>(() => PlcDeviceData.DecodeWrite(item, WorkerJson.SerializePayload(result)));

        var folder = new PlcOperationRequest { OperationId = "f", Operation = PlcDeviceDataMethods.DownloadToFolder, DirectoryPath = @"C:\Download" };
        var empty = new PlcDeviceDataResultInfo { Operation = folder.Operation, PlcName = "PLC_1", Call = "DownloadProvider.Download(DirectoryInfo, DownloadConfigurationDelegate)", Evidence = "verified" };
        Assert.ThrowsAny<JsonException>(() => PlcDeviceData.DecodeWrite(folder, WorkerJson.SerializePayload(empty)));
    }

    [Fact]
    public async Task FakeWorker_DownloadsToAFolder_AndTheOnlineSecretIsNotVerified()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip", McpAccessMode.Full);
        var folder = await fixture.RunAsync(false,
            new PlcOperationRequest { OperationId = "f", Operation = PlcDeviceDataMethods.DownloadToFolder, PlcName = "Station_1", DirectoryPath = @"C:\Download", RhSystem = true });
        Assert.True(folder.Success, JsonSerializer.Serialize(folder.Error));
        Assert.True(Assert.Single(folder.Verification!.Operations).Success);

        var reset = await fixture.RunAsync(false, new PlcOperationRequest { OperationId = "r", Operation = PlcDeviceDataMethods.ResetMasterSecretOnline, PlcName = "Station_1" });
        Assert.Contains(reset.Guards, g => g.Id == PlcGuardDefinitions.MasterSecretOnline);
        Assert.False(reset.Success);
        Assert.False(Assert.Single(reset.Verification!.Operations).Success);

        var offline = await fixture.RunAsync(false, new PlcOperationRequest { OperationId = "o", Operation = PlcDeviceDataMethods.ResetMasterSecretOnline, PlcName = "PLC_2" });
        Assert.Equal("state_changed", offline.Batch!.Operations.Single().Failure!.Category);
        var rh = await fixture.RunAsync(false,
            new PlcOperationRequest { OperationId = "h", Operation = PlcDeviceDataMethods.DownloadToFolder, PlcName = "PLC_2", DirectoryPath = @"C:\Download", RhSystem = true });
        Assert.Equal("unsupported_capability", rh.Batch!.Operations.Single().Failure!.Category);
    }

    [Fact]
    public async Task FakeWorker_ActualValueLoads_FireTheAcknowledgeGuard_AreNeverVerified_AndNeedAnOnlinePlc()
    {
        Assert.All(PlcDeviceDataMethods.ActualValues, method => Assert.Equal(OperationCapability.OnlineControl, OperationPolicyCatalog.GetCapability(method)));
        Assert.Contains(PlcGuardDefinitions.Definitions, d => d.Id == PlcGuardDefinitions.LoadsActualValuesOnline && d.Severity == TiaMcpServer.Safety.Pipeline.WriteGuardSeverities.Acknowledge);
        Assert.False(PlcOperationCatalog.ValidateWrite(new[] { new PlcOperationRequest { OperationId = "a", Operation = PlcDeviceDataMethods.LoadSnapshotAsActualValues } }).IsValid);

        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip", McpAccessMode.Full);
        foreach (var method in PlcDeviceDataMethods.ActualValues)
        {
            var load = await fixture.RunAsync(false, new PlcOperationRequest { OperationId = "v", Operation = method, BlockPath = "Station_1/Data_block_1" });
            Assert.Contains(load.Guards, g => g.Id == PlcGuardDefinitions.LoadsActualValuesOnline);
            var entry = Assert.Single(load.Verification!.Operations);
            Assert.False(entry.Success);
            Assert.Contains("nothing documented reads it back", entry.Message, StringComparison.Ordinal);
            Assert.Null(load.Batch!.Operations.Single().Failure);
        }

        var offline = await fixture.RunAsync(false, new PlcOperationRequest { OperationId = "o", Operation = PlcDeviceDataMethods.LoadStartValuesAsActualValues, BlockPath = "PLC_2/Data_block_1" });
        Assert.Equal("state_changed", offline.Batch!.Operations.Single().Failure!.Category);
    }

    [Fact]
    public void TargetDirectory_NonEmptyIsRefusedWithoutOverwrite_EmptyAndOverwriteAreAccepted()
    {
        var root = Path.Combine(Path.GetTempPath(), "tiamcp-f3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Null(PlcDeviceDataPolicy.TargetError(root, overwrite: false).Refusal);
            File.WriteAllText(Path.Combine(root, "old.dat"), "x");
            Assert.Contains("not empty", PlcDeviceDataPolicy.TargetError(root, overwrite: false).Refusal);
            Assert.Null(PlcDeviceDataPolicy.TargetError(root, overwrite: true).Refusal);
            Directory.CreateDirectory(Path.Combine(root, "only-a-folder"));
            File.Delete(Path.Combine(root, "old.dat"));
            Assert.Contains("not empty", PlcDeviceDataPolicy.TargetError(root, overwrite: false).Refusal);
            Assert.Contains("does not exist", PlcDeviceDataPolicy.TargetError(Path.Combine(root, "missing"), overwrite: true).Refusal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Confirmations_DeclinedFails_AcceptedWarns()
    {
        var no = new TiaMcpServer.Contracts.Plc.PlcDownloadPortalConfirmationInfo { Type = "T", Text = "Compile?", Answer = "No" };
        var yes = new TiaMcpServer.Contracts.Plc.PlcDownloadPortalConfirmationInfo { Type = "T", Text = "Compile?", Answer = "Yes" };
        Assert.Null(PlcDeviceDataPolicy.Declined("op", Array.Empty<TiaMcpServer.Contracts.Plc.PlcDownloadPortalConfirmationInfo>(), 0));
        Assert.Contains("answered No", PlcDeviceDataPolicy.Declined("op", new[] { no }, 1));
        // Dialogs beyond the recorded cap under decline were answered No too.
        Assert.NotNull(PlcDeviceDataPolicy.Declined("op", Array.Empty<TiaMcpServer.Contracts.Plc.PlcDownloadPortalConfirmationInfo>(), 3));
        Assert.Null(PlcDeviceDataPolicy.Declined("op", new[] { yes }, 1));
        Assert.Contains("answered Yes", PlcDeviceDataPolicy.Accepted("op", new[] { yes }));
        Assert.Null(PlcDeviceDataPolicy.Accepted("op", new[] { no }));
    }

    [Fact]
    public void Catalog_OverwriteOnTheFolderDownload_PortalConfirmationsOnTheMasterSecret()
    {
        Assert.True(PlcOperationCatalog.ValidateWrite(new[] { new PlcOperationRequest { OperationId = "f", Operation = PlcDeviceDataMethods.DownloadToFolder, DirectoryPath = @"C:\Download", Overwrite = true } }).IsValid);
        Assert.True(PlcOperationCatalog.ValidateWrite(new[] { new PlcOperationRequest { OperationId = "r", Operation = PlcDeviceDataMethods.ResetMasterSecretOnline, PortalConfirmations = "accept" } }).IsValid);
        Assert.False(PlcOperationCatalog.ValidateWrite(new[] { new PlcOperationRequest { OperationId = "r", Operation = PlcDeviceDataMethods.ResetMasterSecretOnline, Overwrite = true } }).IsValid);
        var plc = new TiaMcpServer.Contracts.Plc.PlcTagInventoryPlcInfo { PlcName = "PLC_1", DeviceName = "Station_1" };
        var guarded = PlcDeviceData.Resolve(new PlcOperationRequest { OperationId = "f", Operation = PlcDeviceDataMethods.DownloadToFolder, DirectoryPath = @"C:\Download", Overwrite = true }, plc);
        Assert.Contains(guarded.Guards, g => g.Id == PlcGuardDefinitions.ExportOverwritesFile);
        Assert.True(PlcDeviceData.ToRequest(new PlcOperationRequest { OperationId = "f", Operation = PlcDeviceDataMethods.DownloadToFolder, DirectoryPath = @"C:\Download", Overwrite = true }).Overwrite);
        Assert.Equal("accept", PlcDeviceData.ToRequest(new PlcOperationRequest { OperationId = "r", Operation = PlcDeviceDataMethods.ResetMasterSecretOnline, PortalConfirmations = "accept" }).PortalConfirmations);
    }

    [Fact]
    public async Task FakeWorker_NonEmptyFolderNeedsOverwrite_AndDialogsFollowPortalConfirmations()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip", McpAccessMode.Full);
        var refused = await fixture.RunAsync(false,
            new PlcOperationRequest { OperationId = "f", Operation = PlcDeviceDataMethods.DownloadToFolder, PlcName = "Station_1", DirectoryPath = @"C:\DownloadFull" });
        Assert.Equal("validation_error", refused.Batch!.Operations.Single().Failure!.Category);
        var overwritten = await fixture.RunAsync(false,
            new PlcOperationRequest { OperationId = "f", Operation = PlcDeviceDataMethods.DownloadToFolder, PlcName = "Station_1", DirectoryPath = @"C:\DownloadFull", Overwrite = true });
        Assert.True(overwritten.Success, JsonSerializer.Serialize(overwritten.Error));

        var declined = await fixture.RunAsync(false,
            new PlcOperationRequest { OperationId = "d", Operation = PlcDeviceDataMethods.DownloadToFolder, PlcName = "Station_1", DirectoryPath = @"C:\DownloadDialog" });
        Assert.Contains("answered No", declined.Batch!.Operations.Single().Failure!.Message);
        var accepted = await fixture.RunAsync(false,
            new PlcOperationRequest { OperationId = "a", Operation = PlcDeviceDataMethods.DownloadToFolder, PlcName = "Station_1", DirectoryPath = @"C:\DownloadDialog", PortalConfirmations = "accept" });
        Assert.True(accepted.Success, JsonSerializer.Serialize(accepted.Error));
        var result = accepted.Batch!.Operations.Single().Result!.Value;
        Assert.Equal("Yes", result.GetProperty("portalConfirmations")[0].GetProperty("answer").GetString());
    }
}
