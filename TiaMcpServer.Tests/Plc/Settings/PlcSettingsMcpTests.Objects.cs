using System.Text.Json;
using TiaMcpServer.Contracts.Plc.Settings;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Plc;
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
}
