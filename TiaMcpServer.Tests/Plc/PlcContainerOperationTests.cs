using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Plc;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

/// <summary>
/// Siemens-free rules of the PLC data type group, tag table group and software unit containers, read_tag_comments
/// and the externalSources library target.
/// </summary>
public sealed class PlcContainerOperationTests
{
    private static PlcOperationRequest Op(string operation, Action<PlcOperationRequest>? set = null)
    {
        var request = new PlcOperationRequest { OperationId = "a", Operation = operation };
        set?.Invoke(request);
        return request;
    }

    private static string? ReadError(PlcOperationRequest op) => PlcOperationCatalog.ValidateRead(new[] { op }) is { IsValid: false } invalid ? invalid.Error : null;

    private static string? WriteError(PlcOperationRequest op) => PlcOperationCatalog.ValidateWrite(new[] { op }) is { IsValid: false } invalid ? invalid.Error : null;

    [Fact]
    public void ContainerOperationsAreProjectMutationsOnPlcWrite()
    {
        foreach (var method in PlcSourceFolderMethods.GroupCreatesDeletes.Concat(PlcSourceFolderMethods.SoftwareUnitOperations))
        {
            Assert.Equal(OperationCapability.ProjectMutation, OperationPolicyCatalog.GetCapability(method));
            Assert.Equal("plc_write", ProjectCapabilityCatalog.OperationFor(method));
            Assert.True(PlcSourceFolderMethods.IsWorkerMethod(method));
            Assert.Contains(method, PlcOperationCatalog.WriteOperationNames);
            Assert.True(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.LocalSession, method));
        }

        Assert.Equal(OperationCapability.Observe, OperationPolicyCatalog.GetCapability(PlcSourceFolderMethods.ReadTagComments));
        Assert.Equal("plc_read", ProjectCapabilityCatalog.OperationFor(PlcSourceFolderMethods.ReadTagComments));
        Assert.Contains(PlcSourceFolderMethods.ReadTagComments, PlcOperationCatalog.ReadOperationNames);
    }

    [Fact]
    public void ValidContainerItemsPass()
    {
        Assert.Null(WriteError(Op(PlcSourceFolderMethods.CreateTypeGroup, o => o.TypePath = "PLC_1/Types/Motion")));
        Assert.Null(WriteError(Op(PlcSourceFolderMethods.DeleteTypeGroup, o => o.TypePath = "PLC_1/Units/U1/Types/Motion/Drives")));
        Assert.Null(WriteError(Op(PlcSourceFolderMethods.CreateTagTableGroup, o => { o.FolderPath = "/Line/Cell"; o.UnitName = "U1"; o.PlcName = "PLC_1"; })));
        Assert.Null(WriteError(Op(PlcSourceFolderMethods.DeleteTagTableGroup, o => o.FolderPath = "/Line")));
        Assert.Null(WriteError(Op(PlcSourceFolderMethods.CreateSoftwareUnit, o => o.UnitName = "Conveyor")));
        Assert.Null(WriteError(Op(PlcSourceFolderMethods.DeleteSoftwareUnit, o => { o.UnitName = "Conveyor"; o.PlcName = "PLC_1"; })));
        Assert.Null(WriteError(Op(PlcSourceFolderMethods.RenameSoftwareUnit, o => { o.UnitName = "Conveyor"; o.NewName = "Belt"; })));
        Assert.Null(ReadError(Op(PlcSourceFolderMethods.ReadTagComments)));
        Assert.Null(ReadError(Op(PlcSourceFolderMethods.ReadTagComments, o => { o.PlcName = "PLC_1"; o.TableName = "Motors"; o.FolderPath = "/Line"; })));
    }

    [Fact]
    public void ContainerShapeErrorsAreRefusedBeforeAnyWorkerCall()
    {
        Assert.Contains("only user groups can be created", WriteError(Op(PlcSourceFolderMethods.CreateTypeGroup, o => o.TypePath = "PLC_1/Types")));
        Assert.Contains("only user groups can be deleted", WriteError(Op(PlcSourceFolderMethods.DeleteTypeGroup, o => o.TypePath = "PLC_1/Units/U1/Types")));
        Assert.Contains("PLC data type group path", WriteError(Op(PlcSourceFolderMethods.CreateTypeGroup, o => o.TypePath = "PLC_1/Blocks/A")));
        Assert.Contains("'plcName' is not valid", WriteError(Op(PlcSourceFolderMethods.CreateTypeGroup, o => { o.TypePath = "PLC_1/Types/A"; o.PlcName = "PLC_1"; })));
        Assert.Contains("cannot be created", WriteError(Op(PlcSourceFolderMethods.CreateTagTableGroup, o => o.FolderPath = "/")));
        Assert.Contains("cannot be deleted", WriteError(Op(PlcSourceFolderMethods.DeleteTagTableGroup, o => o.FolderPath = " / ")));
        Assert.Contains("must not contain '\\'", WriteError(Op(PlcSourceFolderMethods.CreateTagTableGroup, o => o.FolderPath = @"/A\B")));
        Assert.Contains("missing required field(s): folderPath", WriteError(Op(PlcSourceFolderMethods.DeleteTagTableGroup)));
        Assert.Contains("unitName 'A/B' must not contain", WriteError(Op(PlcSourceFolderMethods.CreateSoftwareUnit, o => o.UnitName = "A/B")));
        Assert.Contains("missing required field(s): newName", WriteError(Op(PlcSourceFolderMethods.RenameSoftwareUnit, o => o.UnitName = "U1")));
        Assert.Contains("newName 'x\\y' must not contain", WriteError(Op(PlcSourceFolderMethods.RenameSoftwareUnit, o => { o.UnitName = "U1"; o.NewName = @"x\y"; })));
        Assert.Contains("without surrounding blanks", WriteError(Op(PlcSourceFolderMethods.DeleteSoftwareUnit, o => o.UnitName = " U1")));
        Assert.Contains("is a read operation", WriteError(Op(PlcSourceFolderMethods.ReadTagComments)));
        Assert.Contains("'newName' is not valid", ReadError(Op(PlcSourceFolderMethods.ReadTagComments, o => o.NewName = "x")));
    }

    [Fact]
    public void RequestCarriesTheGroupPathAndTheCommentFilters()
    {
        Assert.Equal("PLC_1/Types/A", PlcSourceFolderOperations.Request(Op(PlcSourceFolderMethods.CreateTypeGroup, o => o.TypePath = "PLC_1/Types/A")).GroupPath);
        Assert.Equal("PLC_1/Types/A", PlcSourceFolderOperations.Request(Op(PlcSourceFolderMethods.DeleteTypeGroup, o => o.TypePath = "PLC_1/Types/A")).GroupPath);
        var tag = PlcSourceFolderOperations.Request(Op(PlcSourceFolderMethods.CreateTagTableGroup, o => { o.FolderPath = "/A"; o.UnitName = "U1"; }));
        Assert.Equal(("/A", "U1", (string?)null), (tag.FolderPath, tag.UnitName, tag.GroupPath));
        var comments = PlcSourceFolderOperations.Request(Op(PlcSourceFolderMethods.ReadTagComments, o => { o.TableName = "T"; o.FolderPath = "/A"; }));
        Assert.Equal(("T", "/A"), (comments.TableName, comments.FolderPath));
        Assert.Null(PlcSourceFolderOperations.Request(Op("create_tag_table", o => o.TableName = "T")).TableName);
    }

    private static string Container(string operation, string kind, string name, string? unit = null) => WorkerJson.SerializePayload(new PlcContainerMutationInfo
    {
        Operation = operation, Kind = kind, PlcName = "PLC_1", DeviceName = "S1", UnitName = unit, Path = "p", Name = name,
    });

    [Fact]
    public void ContainerResultsMustMatchTheRequest()
    {
        var create = Op(PlcSourceFolderMethods.CreateTypeGroup, o => o.TypePath = "PLC_1/Types/Motion/Drives");
        Assert.Equal("Drives", PlcSourceFolderOperations.DecodeWrite(create, Container(create.Operation, PlcSourceFolderKinds.TypeGroup, "Drives")).GetProperty("name").GetString());
        Assert.Throws<JsonException>(() => PlcSourceFolderOperations.DecodeWrite(create, Container(create.Operation, PlcSourceFolderKinds.TypeGroup, "Motion")));
        Assert.Throws<JsonException>(() => PlcSourceFolderOperations.DecodeWrite(create, Container(create.Operation, PlcSourceFolderKinds.TagTableGroup, "Drives")));
        Assert.Throws<JsonException>(() => PlcSourceFolderOperations.DecodeWrite(create, Container(PlcSourceFolderMethods.DeleteTypeGroup, PlcSourceFolderKinds.TypeGroup, "Drives")));

        var unit = Op(PlcSourceFolderMethods.CreateSoftwareUnit, o => o.UnitName = "U1");
        Assert.Equal("U1", PlcSourceFolderOperations.DecodeWrite(unit, Container(unit.Operation, PlcSourceFolderKinds.SoftwareUnit, "U1", "U1")).GetProperty("unitName").GetString());
        Assert.Throws<JsonException>(() => PlcSourceFolderOperations.DecodeWrite(unit, Container(unit.Operation, PlcSourceFolderKinds.SoftwareUnit, "U1", null)));

        var delete = Op(PlcSourceFolderMethods.DeleteTagTableGroup, o => o.FolderPath = "/A");
        Assert.Equal("A", PlcSourceFolderOperations.DecodeWrite(delete, Container(delete.Operation, PlcSourceFolderKinds.TagTableGroup, "A")).GetProperty("name").GetString());
        Assert.Throws<JsonException>(() => PlcSourceFolderOperations.DecodeWrite(delete, Container(delete.Operation, PlcSourceFolderKinds.TagTableGroup, "")));

        var rename = Op(PlcSourceFolderMethods.RenameSoftwareUnit, o => { o.UnitName = "U1"; o.NewName = "U2"; });
        string Rename(string newName) => WorkerJson.SerializePayload(new PlcGroupRenameInfo
        {
            Operation = PlcSourceFolderMethods.RenameSoftwareUnit, Kind = PlcSourceFolderKinds.SoftwareUnit, PlcName = "PLC_1", DeviceName = "S1", UnitName = "U1",
            Path = "PLC_1/Units/U1", NewPath = $"PLC_1/Units/{newName}", OldName = "U1", NewName = newName,
        });
        Assert.Equal("U2", PlcSourceFolderOperations.DecodeWrite(rename, Rename("U2")).GetProperty("newName").GetString());
        Assert.Throws<JsonException>(() => PlcSourceFolderOperations.DecodeWrite(rename, Rename("U1")));
    }

    [Fact]
    public void CommentReadsMustKeepTheirContract()
    {
        var op = Op(PlcSourceFolderMethods.ReadTagComments, o => o.TableName = "Motors");
        PlcTagCommentInventoryInfo Inventory(string table, string kind, bool complete, List<string> messages) => new()
        {
            Complete = complete, Messages = messages,
            Plcs = new()
            {
                new PlcTagCommentPlcInfo
                {
                    PlcName = "PLC_1", DeviceName = "S1",
                    Tables = new() { new PlcTagTableCommentsInfo { FolderPath = "/Line", TableName = table, Members = new() { new PlcMemberCommentInfo { Kind = kind, Name = "M", Comment = null } } } },
                },
            },
        };
        var ok = PlcSourceFolderOperations.DecodeRead(op, WorkerJson.SerializePayload(Inventory("motors", "Tag", true, new())));
        Assert.Equal(JsonValueKind.Null, ok.GetProperty("plcs")[0].GetProperty("tables")[0].GetProperty("members")[0].GetProperty("comment").ValueKind);
        Assert.Throws<JsonException>(() => PlcSourceFolderOperations.DecodeRead(op, WorkerJson.SerializePayload(Inventory("Valves", "Tag", true, new()))));
        Assert.Throws<JsonException>(() => PlcSourceFolderOperations.DecodeRead(op, WorkerJson.SerializePayload(Inventory("Motors", "Constant", true, new()))));
        Assert.Throws<JsonException>(() => PlcSourceFolderOperations.DecodeRead(op, WorkerJson.SerializePayload(Inventory("Motors", "Tag", true, new() { "unreadable" }))));
        Assert.Throws<JsonException>(() => PlcSourceFolderOperations.DecodeRead(op, WorkerJson.SerializePayload(Inventory("Motors", "Tag", false, new()))));
    }

    [Fact]
    public void ExternalSourcesAreAMasterCopyTargetOfThePlc()
    {
        Assert.Contains(LibraryPolicy.TargetKinds.ExternalSources, LibraryPolicy.TargetKinds.MasterCopyTargets);
        Assert.DoesNotContain(LibraryPolicy.TargetKinds.ExternalSources, LibraryPolicy.TargetKinds.TypeVersionTargets);
        Assert.True(LibraryPolicy.TargetKinds.TakesMasterCopyMode(LibraryPolicy.TargetKinds.ExternalSources));
        Assert.True(LibraryPolicy.TargetKinds.NeedsPlc(LibraryPolicy.TargetKinds.ExternalSources));
        Assert.False(LibraryPolicy.TargetKinds.HasFolder(LibraryPolicy.TargetKinds.ExternalSources));
    }

    [Fact]
    public void TheGroupContentsGuardCoversEveryContainerDelete()
    {
        var guard = PlcGuardDefinitions.Definitions.Single(g => g.Id == PlcGuardDefinitions.DeletesGroupContents);
        Assert.Equal("info", guard.Severity);
        foreach (var word in new[] { "block group", "PLC data type group", "tag table group", "software unit" }) Assert.Contains(word, guard.Description);
    }
}
