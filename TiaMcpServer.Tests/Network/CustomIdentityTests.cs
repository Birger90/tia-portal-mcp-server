using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Network;

/// <summary>
/// Issue #18: custom identities on devices and device items (network_read get_custom_identity, network_write
/// set_custom_identity / remove_custom_identity). The Siemens-free run against a stand-in provider, the catalog and policy,
/// and the hardware-modules FakeWorker end to end. Not live-tested: which hardware objects list CustomIdentityProvider and how
/// Get answers a missing key are decided only live.
/// </summary>
[Collection("Mcp protocol serial")]
public sealed class CustomIdentityTests
{
    private sealed class NotFound : Exception;

    private sealed class Provider
    {
        public Dictionary<string, string?> Values { get; } = new(StringComparer.Ordinal);

        public bool IgnoreSet { get; set; }

        public string? Get(string key) => Values.TryGetValue(key, out var value) ? value : throw new Siemens.Engineering.CustomIdentity.CustomIdentityNotFoundException();

        public void Set(string key, string value)
        {
            if (!IgnoreSet) Values[key] = value;
        }

        public void Remove(string key)
        {
            if (!Values.Remove(key)) throw new Siemens.Engineering.CustomIdentity.CustomIdentityNotFoundException();
        }
    }

    [Fact]
    public void Run_GetReportsFoundNotFoundAndNull()
    {
        var provider = new Provider();
        Assert.Equal(CustomIdentityNames.States.NotFound, CustomIdentityRun.Get(provider, "k").State);
        provider.Values["k"] = "v";
        var found = CustomIdentityRun.Get(provider, "k");
        Assert.Equal((CustomIdentityNames.States.Found, "v"), (found.State, found.Value));
        provider.Values["n"] = null;
        Assert.Equal(CustomIdentityNames.States.Null, CustomIdentityRun.Get(provider, "n").State);
        Assert.True(CustomIdentityRun.IsNotFound(new Siemens.Engineering.CustomIdentity.CustomIdentityNotFoundException()));
        Assert.False(CustomIdentityRun.IsNotFound(new NotFound()));
    }

    [Fact]
    public void Run_SetAndRemove_AreReadBack_AndAMissingKeyIsNotFound()
    {
        var provider = new Provider();
        var (before, after) = CustomIdentityRun.Set(provider, "k", "v", "Device 'PLC_1'");
        Assert.Equal(CustomIdentityNames.States.NotFound, before.State);
        Assert.Equal("v", after.Value);

        provider.IgnoreSet = true;
        var ignored = Assert.Throws<RuntimeBindingException>(() => CustomIdentityRun.Set(provider, "k", "w", "Device 'PLC_1'"));
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, ignored.Category);

        var removed = CustomIdentityRun.Remove(provider, "k", "Device 'PLC_1'");
        Assert.Equal(CustomIdentityNames.States.NotFound, removed.After.State);
        var missing = Assert.Throws<RuntimeBindingException>(() => CustomIdentityRun.Remove(provider, "k", "Device 'PLC_1'"));
        Assert.Equal(WorkerFailureCategories.TargetNotFound, missing.Category);
        Assert.Contains("nothing was removed", missing.Message);

        var noMembers = Assert.Throws<RuntimeBindingException>(() => CustomIdentityRun.Get(new object(), "k"));
        Assert.Equal(WorkerFailureCategories.ApiMemberMissing, noMembers.Category);
    }

    [Fact]
    public void Catalog_RequiresKeyAndValue_AndAcceptsADeviceTarget()
    {
        var device = new NetworkObjectTarget { Kind = "device", DeviceName = "PLC_1" };
        NetworkOperationRequest Op(string operation, string? key, string? value = null)
            => new() { OperationId = "c", Operation = operation, Target = device, Key = key, Value = value };

        Assert.True(NetworkOperationCatalog.ValidateRead(new[] { Op(HardwareModuleMethods.GetCustomIdentity, "k") }).IsValid);
        Assert.False(NetworkOperationCatalog.ValidateRead(new[] { Op(HardwareModuleMethods.GetCustomIdentity, null) }).IsValid);
        Assert.False(NetworkOperationCatalog.ValidateRead(new[] { Op(HardwareModuleMethods.GetCustomIdentity, "k", "v") }).IsValid);
        Assert.True(NetworkOperationCatalog.ValidateWrite(new[] { Op(HardwareModuleMethods.SetCustomIdentity, "k", string.Empty) }).IsValid);
        Assert.False(NetworkOperationCatalog.ValidateWrite(new[] { Op(HardwareModuleMethods.SetCustomIdentity, "k") }).IsValid);
        Assert.True(NetworkOperationCatalog.ValidateWrite(new[] { Op(HardwareModuleMethods.RemoveCustomIdentity, "k") }).IsValid);
        Assert.False(NetworkOperationCatalog.ValidateWrite(new[] { Op(HardwareModuleMethods.RemoveCustomIdentity, " ") }).IsValid);
    }

    [Fact]
    public void Policy_ReadObserves_WritesMutate_AndGateAsNetworkTools()
    {
        Assert.Equal(OperationCapability.Observe, OperationPolicyCatalog.GetCapability(HardwareModuleMethods.GetCustomIdentity));
        Assert.Equal("network_read", ProjectCapabilityCatalog.OperationFor(HardwareModuleMethods.GetCustomIdentity));
        foreach (var method in new[] { HardwareModuleMethods.SetCustomIdentity, HardwareModuleMethods.RemoveCustomIdentity })
        {
            Assert.Equal(OperationCapability.ProjectMutation, OperationPolicyCatalog.GetCapability(method));
            Assert.False(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, method));
            Assert.Equal("network_write", ProjectCapabilityCatalog.OperationFor(method));
        }
    }

    private static object Device => new { kind = "device", deviceName = "PLC_1" };

    private static object Dq => new
    {
        kind = "deviceItem", deviceName = "PLC_1",
        itemPath = new object[]
        {
            new { index = 0, name = "Rack_0", positionNumber = 0, typeIdentifier = "System:Rack.S71500" },
            new { index = 2, name = "DQ 16x24VDC_1", positionNumber = 3, typeIdentifier = "OrderNumber:6ES7 522-1BH00-0AB0/V2.1" },
        },
    };

    private static async Task<JsonElement> CallAsync(McpProtocolTestHarness harness, string tool, object[] operations, bool? dryRun = null)
    {
        var arguments = new Dictionary<string, object?> { ["operations"] = operations };
        if (dryRun is not null) arguments["dryRun"] = dryRun;
        var reply = await harness.Client.CallToolAsync(tool, arguments);
        var root = Assert.IsType<JsonElement>(reply.StructuredContent);
        Assert.Equal(CanonicalJson.Serialize(root), Assert.Single(reply.Content.OfType<TextContentBlock>()).Text);
        return root;
    }

    private static JsonElement Item(JsonElement root, int index = 0) => root.GetProperty("batch").GetProperty("operations")[index];

    [Fact]
    public async Task Mcp_SetReadRemove_OnADevice_AndADeviceItemWithoutTheService()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, "hardware-modules");

        var missing = await CallAsync(harness, "network_read", new object[] { new { operationId = "g", operation = "get_custom_identity", target = Device, key = "MES.Id" } });
        Assert.Equal("succeeded", Item(missing).GetProperty("status").GetString());
        Assert.Equal("notFound", Item(missing).GetProperty("result").GetProperty("observed").GetProperty("state").GetString());

        var set = new { operationId = "s", operation = "set_custom_identity", target = Device, key = "MES.Id", value = "Line-7" };
        var preview = await CallAsync(harness, "network_write", new object[] { set }, dryRun: true);
        Assert.Equal("preview", preview.GetProperty("phase").GetString());
        var effect = preview.GetProperty("effects")[0].GetProperty("effect");
        Assert.Equal("notFound", effect.GetProperty("currentSettings").GetProperty("state").GetString());
        Assert.Equal("Line-7", effect.GetProperty("requestedSettings").GetProperty("value").GetString());

        var applied = await CallAsync(harness, "network_write", new object[] { set });
        Assert.True(applied.GetProperty("success").GetBoolean(), applied.GetRawText());
        var result = Item(applied).GetProperty("result");
        Assert.Equal("Line-7", result.GetProperty("after").GetProperty("value").GetString());
        // K78: the worker's read-back with Get is the verification (no longer "trusted ... No separate verification read followed").
        var setEntry = applied.GetProperty("verification").GetProperty("operations")[0];
        Assert.Equal(("customIdentity", true, "MES.Id=Line-7", "MES.Id=Line-7"), (setEntry.GetProperty("check").GetString(),
            setEntry.GetProperty("success").GetBoolean(), setEntry.GetProperty("expected").GetString(), setEntry.GetProperty("actual").GetString()));

        var read = await CallAsync(harness, "network_read", new object[] { new { operationId = "g", operation = "get_custom_identity", target = Device, key = "MES.Id" } });
        Assert.Equal("Line-7", Item(read).GetProperty("result").GetProperty("observed").GetProperty("value").GetString());

        var removed = await CallAsync(harness, "network_write", new object[] { new { operationId = "r", operation = "remove_custom_identity", target = Device, key = "MES.Id" } });
        Assert.True(removed.GetProperty("success").GetBoolean(), removed.GetRawText());
        Assert.Equal("notFound", Item(removed).GetProperty("result").GetProperty("after").GetProperty("state").GetString());
        var removeEntry = removed.GetProperty("verification").GetProperty("operations")[0];
        Assert.Equal(("customIdentity", true, "MES.Id absent"), (removeEntry.GetProperty("check").GetString(),
            removeEntry.GetProperty("success").GetBoolean(), removeEntry.GetProperty("actual").GetString()));

        // K78: a set then a remove of the same key in one call: the set is superseded by a verified remove, the remove is verified.
        var both = await CallAsync(harness, "network_write", new object[] { set, new { operationId = "r2", operation = "remove_custom_identity", target = Device, key = "MES.Id" } });
        Assert.True(both.GetProperty("success").GetBoolean(), both.GetRawText());
        var verification = both.GetProperty("verification");
        Assert.True(verification.GetProperty("success").GetBoolean(), both.GetRawText());
        Assert.Equal("superseded", verification.GetProperty("operations")[0].GetProperty("check").GetString());
        Assert.Contains("the final state is verified there", verification.GetProperty("operations")[0].GetProperty("message").GetString());
        Assert.Equal(("customIdentity", true), (verification.GetProperty("operations")[1].GetProperty("check").GetString(),
            verification.GetProperty("operations")[1].GetProperty("success").GetBoolean()));

        var again = await CallAsync(harness, "network_write", new object[] { new { operationId = "r", operation = "remove_custom_identity", target = Device, key = "MES.Id" } });
        Assert.False(again.GetProperty("success").GetBoolean());
        Assert.Equal("target_not_found", Item(again).GetProperty("failure").GetProperty("category").GetString());

        var unsupported = await CallAsync(harness, "network_write", new object[] { new { operationId = "q", operation = "set_custom_identity", target = Dq, key = "k", value = "v" } });
        Assert.Equal("error", unsupported.GetProperty("phase").GetString());
        Assert.Equal("unsupported_capability", unsupported.GetProperty("error").GetProperty("category").GetString());
    }
}
