using System.Text.Json;
using TiaMcpServer.Contracts.Plc.Settings;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Plc;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Plc.Settings;

/// <summary>Package G: engineering objects by path (markings, identifiers, creation infos, linked tags, cam data) against plc-settings stand-ins.</summary>
public sealed partial class PlcSettingsMcpTests
{
    [Theory]
    [InlineData("project", true)]
    [InlineData("block:PLC_1/Main", true)]
    [InlineData("channel:S/M/Digital/Input/0", true)]
    [InlineData("channel:S/M/Digital/Input/x", false)]
    [InlineData("tagTable:PLC_1", false)]
    [InlineData("folder:x", false)]
    [InlineData("plc:", false)]
    public void Objects_PathGrammar(string path, bool valid)
        => Assert.Equal(valid, PlcObjectPaths.Parse(path).Error is null);

    [Fact]
    public void Objects_Catalog_Policy_AndShapes()
    {
        foreach (var method in new[] { PlcObjectMethods.GetMarkings, PlcObjectMethods.GetMarkState, PlcObjectMethods.GetObjectIdentifier, PlcObjectMethods.FindObjectByIdentifier, PlcObjectMethods.GetCreationInfos, PlcObjectMethods.GetLinkedTags })
            Assert.Equal(OperationCapability.Observe, OperationPolicyCatalog.GetCapability(method));
        foreach (var method in new[] { PlcObjectMethods.MarkObjects, PlcObjectMethods.UnmarkObjects, PlcObjectMethods.SaveCamData, PlcObjectMethods.LoadCamData })
            Assert.Equal(OperationCapability.ProjectMutation, OperationPolicyCatalog.GetCapability(method));
        var pointList = PlcOperationCatalog.ValidateWrite(new[] { new PlcOperationRequest { OperationId = "c", Operation = PlcObjectMethods.SaveCamData, ObjectPath = "technologyObject:PLC_1/Cam_1", FilePath = Path.Combine(Path.GetTempPath(), "cam.csv"), CamFormat = "PointList" } });
        Assert.False(pointList.IsValid);
        Assert.Contains("samplePoints", pointList.Error, StringComparison.Ordinal);
        var wrongKind = PlcOperationCatalog.ValidateWrite(new[] { new PlcOperationRequest { OperationId = "c", Operation = PlcObjectMethods.LoadCamData, ObjectPath = "block:PLC_1/Main", FilePath = Path.Combine(Path.GetTempPath(), "cam.csv") } });
        Assert.Contains("technologyObject", wrongKind.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Objects_MarkingsNeedALocalSession()
    {
        var context = new PlcSettingsContext { ResolveObject = _ => new object() };
        var refusal = Assert.Throws<RuntimeBindingException>(() => PlcSettingsRun.Write(PlcObjectMethods.MarkObjects, context, new PlcSettingsRequestInfo { ObjectPaths = new() { "project" } }));
        Assert.Equal("unsupported_capability", refusal.Category);
    }

    [Fact]
    public async Task Objects_MarkReadIdentifyCreateAndCamData_ThroughTheTools()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);
        var (marked, raw) = await session.WriteAsync(false, new { operationId = "m", operation = "mark_objects", objectPaths = new[] { "block:PLC_1/Main", "technologyObject:PLC_1/Cam_1" } });
        Assert.False(raw.IsError == true, marked.GetRawText());
        Assert.True(Verification(marked, "m").GetProperty("success").GetBoolean(), marked.GetRawText());
        var (refused, _) = await session.WriteAsync(false, new { operationId = "d", operation = "mark_objects", objectPaths = new[] { "device:Station_1" } });
        Assert.False(refused.GetProperty("success").GetBoolean());

        var (markings, _) = await session.ReadAsync(new { operationId = "r", operation = "get_markings" });
        var all = Section(Item(markings, 0).GetProperty("result").GetProperty("project"), "get_markings").GetProperty("collections")[0];
        Assert.Equal(2, all.GetProperty("items").GetArrayLength());
        var (state, _) = await session.ReadAsync(new { operationId = "s", operation = "get_mark_state", objectPath = "block:PLC_1/Main" });
        Assert.Equal("IsMarkedByMe", Property(Section(Item(state, 0).GetProperty("result").GetProperty("project"), "get_mark_state"), "MarkState"));
        var (unmarked, _) = await session.WriteAsync(false, new { operationId = "u", operation = "unmark_objects", objectPaths = new[] { "block:PLC_1/Main" } });
        Assert.True(Verification(unmarked, "u").GetProperty("success").GetBoolean(), unmarked.GetRawText());

        var (identifier, _) = await session.ReadAsync(new { operationId = "i", operation = "get_object_identifier", objectPath = "block:PLC_1/Main" });
        Assert.Equal("id:Main", Property(Section(Item(identifier, 0).GetProperty("result").GetProperty("project"), "get_object_identifier"), "Identifier"));
        var (found, _) = await session.ReadAsync(new { operationId = "f", operation = "find_object_by_identifier", name = "id:Cam_1" });
        Assert.Equal("True", Property(Section(Item(found, 0).GetProperty("result").GetProperty("project"), "find_object_by_identifier"), "Found"));
        var (creation, _) = await session.ReadAsync(new { operationId = "c", operation = "get_creation_infos", objectPath = "block:PLC_1/Main" });
        Assert.Contains("Name (mandatory): System.String", creation.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("Siemens.Engineering.FakeComposition", creation.GetRawText(), StringComparison.Ordinal);
        var (tags, _) = await session.ReadAsync(new { operationId = "t", operation = "get_linked_tags", objectPath = "channel:Station_1/DI 16x24VDC/Digital/Input/0" });
        Assert.Contains("%I0.0", tags.GetRawText(), StringComparison.Ordinal);

        var directory = Path.Combine(Path.GetTempPath(), "tiamcp-cam-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "cam.csv");
            var (saved, savedRaw) = await session.WriteAsync(false,
                new { operationId = "p", operation = "save_cam_data", objectPath = "technologyObject:PLC_1/Cam_1", filePath = file, camFormat = "PointList", camSeparator = "Comma", samplePoints = 3 });
            Assert.False(savedRaw.IsError == true, saved.GetRawText());
            Assert.True(saved.GetProperty("success").GetBoolean(), saved.GetRawText());
            Assert.Equal(3, File.ReadAllLines(file).Length);
            var (loaded, _) = await session.WriteAsync(false, new { operationId = "l", operation = "load_cam_data", objectPath = "technologyObject:PLC_1/Cam_1", filePath = file });
            Assert.True(loaded.GetProperty("success").GetBoolean(), loaded.GetRawText());
            Assert.Contains("plc_loads_cam_data", GuardIds(loaded));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // ----- K92: the worker's own payload through the host's declared result contract, standalone and Multiuser -----

    /// <summary>What the worker answers: the engine's read serialized as on the wire, or its refusal as a failed call.</summary>
    private static WorkerCallResult WorkerAnswer(string method, PlcSettingsContext context, PlcSettingsRequestInfo info)
    {
        try
        {
            return WorkerCallResult.Ok(WorkerJson.SerializePayload(PlcSettingsRun.Read(method, Array.Empty<PlcSettingsContext>(), context, info)));
        }
        catch (RuntimeBindingException exception)
        {
            return WorkerCallResult.Fail(exception.Category, exception.Message);
        }
    }

    private static StructuredOperationItem HostProjection(string method, PlcSettingsContext context, PlcSettingsRequestInfo info, string? objectPath = null)
        => PlcPayloadContract.Project(new PlcOperationRequest { OperationId = "r", Operation = method, ObjectPath = objectPath }, WorkerAnswer(method, context, info), _ => { });

    [Theory]
    [InlineData("get_markings", null)]
    [InlineData("get_mark_state", "block:PLC_1/FC_Zaehlen")]
    public void Objects_Standalone_MarkingReadsAreUnsupportedCapability_NotProtocolError(string method, string? objectPath)
    {
        var context = new PlcSettingsContext { ResolveObject = _ => new object(), EngineeringRoot = new object() };
        var item = HostProjection(method, context, new PlcSettingsRequestInfo { ObjectPath = objectPath }, objectPath);
        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.Equal(WorkerFailureCategories.UnsupportedCapability, item.Failure!.Category);
        Assert.Contains("Multiuser local session", item.Failure.Message, StringComparison.Ordinal);
        Assert.Contains("standalone project", item.Failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("get_object_identifier", "block:PLC_1/Main")]
    [InlineData("find_object_by_identifier", null)]
    [InlineData("get_linked_tags", "channel:S/M/Digital/Input/0")]
    public void Objects_UnofferedServiceSection_PassesTheHostContract(string method, string? objectPath)
    {
        // No service is listed: the section is unavailable and names no owner (the host contract rejects an owner on an unoffered section).
        var context = new PlcSettingsContext { ResolveObject = _ => new object(), EngineeringRoot = new object() };
        var item = HostProjection(method, context, new PlcSettingsRequestInfo { ObjectPath = objectPath, Name = "id:x" }, objectPath);
        Assert.True(item.Status == OperationBatchStatus.Succeeded, item.Failure?.Message);
        var section = Section(item.Result!.Value.GetProperty("project"), method);
        Assert.False(section.GetProperty("offered").GetBoolean());
        Assert.Equal(JsonValueKind.Null, section.GetProperty("owner").ValueKind);
        Assert.False(string.IsNullOrEmpty(section.GetProperty("unavailable").GetString()));
    }

    [Fact]
    public void Objects_LocalSession_MarkingReadsPassTheHostContract()
    {
        var block = new MarkTarget("Main");
        var context = new PlcSettingsContext
        {
            ResolveObject = _ => block, EngineeringRoot = new object(), LocalSession = new MarkSession(block),
            DescribeObject = value => value is MarkTarget target ? $"PlcBlock '{target.Name}'" : "(null)",
        };
        var markings = HostProjection("get_markings", context, new PlcSettingsRequestInfo());
        Assert.True(markings.Status == OperationBatchStatus.Succeeded, markings.Failure?.Message);
        var section = Section(markings.Result!.Value.GetProperty("project"), "get_markings");
        Assert.True(section.GetProperty("offered").GetBoolean());
        Assert.Equal(PlcSettingsNames.ProjectOwner, section.GetProperty("owner").GetString());
        Assert.Equal(1, section.GetProperty("collections")[0].GetProperty("items").GetArrayLength());

        var state = HostProjection("get_mark_state", context, new PlcSettingsRequestInfo { ObjectPath = "block:PLC_1/Main" }, "block:PLC_1/Main");
        Assert.True(state.Status == OperationBatchStatus.Succeeded, state.Failure?.Message);
        Assert.Equal("IsMarkedByMe", Property(Section(state.Result!.Value.GetProperty("project"), "get_mark_state"), "MarkState"));
    }

    [Fact]
    public void Objects_CreationInfos_OfASingleObjectMember_AreEmptyAndNameTheMemberType()
    {
        // K94 (live V21: plc:PLC_1 listed BlockGroup ... WatchAndForceTableGroup, each with no creation infos): an empty list is
        // passed on as TIA Portal returned it, with the composition's EngineeringCompositionInfo.Type beside it.
        var context = new PlcSettingsContext { ResolveObject = _ => new SingleMemberObject(), EngineeringRoot = new object() };
        var item = HostProjection("get_creation_infos", context, new PlcSettingsRequestInfo { ObjectPath = "plc:PLC_1" }, "plc:PLC_1");
        Assert.True(item.Status == OperationBatchStatus.Succeeded, item.Failure?.Message);
        var composition = Section(item.Result!.Value.GetProperty("project"), "get_creation_infos").GetProperty("collections")[0].GetProperty("items")[0];
        Assert.Equal("BlockGroup", composition.GetProperty("key").GetString());
        Assert.Equal(typeof(MarkTarget).FullName, Property(composition, "Type"));
        Assert.Equal(0, composition.GetProperty("collections")[0].GetProperty("items").GetArrayLength());
    }

    public sealed class CompositionInfoStandIn(string name, Type type)
    {
        public string Name { get; } = name;

        public Type Type { get; } = type;
    }

    public sealed class SingleMemberObject
    {
        public IList<CompositionInfoStandIn> GetCompositionInfos() => new[] { new CompositionInfoStandIn("BlockGroup", typeof(MarkTarget)) };

        public IList<object> GetCreationInfos(string compositionName) => Array.Empty<object>();
    }

    [Fact]
    public async Task Objects_StandaloneProject_ThroughTheTools()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite, "plc-settings-standalone");
        var (read, _) = await session.ReadAsync(
            new { operationId = "a", operation = "get_markings" },
            new { operationId = "b", operation = "get_mark_state", objectPath = "block:PLC_1/Main" },
            new { operationId = "c", operation = "get_object_identifier", objectPath = "block:PLC_1/Main" });
        foreach (var index in new[] { 0, 1 })
        {
            var failure = Item(read, index).GetProperty("failure");
            Assert.Equal("unsupported_capability", failure.GetProperty("category").GetString());
            Assert.Contains("Multiuser local session", failure.GetProperty("message").GetString(), StringComparison.Ordinal);
        }

        Assert.Equal("id:Main", Property(Section(Item(read, 2).GetProperty("result").GetProperty("project"), "get_object_identifier"), "Identifier"));
        var (marked, _) = await session.WriteAsync(false, new { operationId = "m", operation = "mark_objects", objectPaths = new[] { "block:PLC_1/Main" } });
        Assert.Equal("unsupported_capability", Item(marked, 0).GetProperty("failure").GetProperty("category").GetString());
    }

    public enum MarkState
    {
        None,
        IsMarkedByMe,
    }

    public sealed class MarkTarget(string name)
    {
        public string Name { get; } = name;
    }

    public sealed class MarkSession(MarkTarget marked)
    {
        public MarkingStandIn MarkingService { get; } = new(marked);
    }

    public sealed class MarkingStandIn(MarkTarget marked)
    {
        public MarkingsStandIn GetMarkings() => new() { AllMarkings = { new MarkingStandInItem { MarkedObject = marked, MarkState = MarkState.IsMarkedByMe } } };

        public MarkStateInfoStandIn GetMarkStateInfo(object target) => new() { MarkState = ReferenceEquals(target, marked) ? MarkState.IsMarkedByMe : MarkState.None, IsMarkable = true };

        public bool IsMarkable(object target) => target is MarkTarget;
    }

    public sealed class MarkingsStandIn
    {
        public List<MarkingStandInItem> AllMarkings { get; } = new();

        public List<MarkingStandInItem> ConflictedMarkings { get; } = new();
    }

    public sealed class MarkingStandInItem
    {
        public object? MarkedObject { get; init; }

        public MarkState MarkState { get; init; }
    }

    public sealed class MarkStateInfoStandIn
    {
        public MarkState MarkState { get; init; }

        public bool IsMarkable { get; init; }
    }
}
