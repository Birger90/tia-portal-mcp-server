using System.Text.Json;
using TiaMcpServer.Contracts.Plc.TechnologyObjects;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Plc;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Plc.TechnologyObjects;

public sealed class PlcTechnologyObjectCatalogTests
{
    private static PlcOperationRequest Op(string operation, Action<PlcOperationRequest>? configure = null)
    {
        var op = new PlcOperationRequest { OperationId = "to", Operation = operation, Name = "Axis_1" };
        configure?.Invoke(op);
        return op;
    }

    private static string? WriteError(PlcOperationRequest op) => PlcOperationCatalog.ValidateWrite(new[] { op }) is { IsValid: false } invalid ? invalid.Error : null;

    private static string? ReadError(PlcOperationRequest op) => PlcOperationCatalog.ValidateRead(new[] { op }) is { IsValid: false } invalid ? invalid.Error : null;

    [Fact]
    public void ReadsAndWritesAreSplitAcrossPlcReadAndPlcWrite()
    {
        Assert.All(TechnologyObjectMethods.Read, name => Assert.Contains(name, PlcOperationCatalog.ReadOperationNames));
        Assert.All(TechnologyObjectMethods.Write, name => Assert.Contains(name, PlcOperationCatalog.WriteOperationNames));
        Assert.Contains("is a write operation; use plc_write", ReadError(Op(TechnologyObjectMethods.Delete)));
        Assert.Contains("is a read operation; use plc_read", WriteError(Op(TechnologyObjectMethods.List, op => op.Name = null)));
    }

    [Fact]
    public void CapabilitiesFollowTheAccessModes()
    {
        Assert.All(TechnologyObjectMethods.Observe, m => Assert.Equal(OperationCapability.Observe, OperationPolicyCatalog.GetCapability(m)));
        Assert.Equal(OperationCapability.TemporaryExport, OperationPolicyCatalog.GetCapability(TechnologyObjectMethods.GetContent));
        Assert.All(TechnologyObjectMethods.Write, m => Assert.Equal(OperationCapability.ProjectMutation, OperationPolicyCatalog.GetCapability(m)));
        Assert.All(TechnologyObjectMethods.Read, m => Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, m)));
        // Export writes a caller file outside TIA Portal: never in read-only, like export_io_map.
        Assert.All(TechnologyObjectMethods.Write, m => Assert.False(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, m)));
        Assert.All(TechnologyObjectMethods.Write, m => Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadWrite, m)));
        Assert.All(TechnologyObjectMethods.Read, m => Assert.Equal("plc_read", ProjectCapabilityCatalog.OperationFor(m)));
        Assert.All(TechnologyObjectMethods.Write, m => Assert.Equal("plc_write", ProjectCapabilityCatalog.OperationFor(m)));
        Assert.All(TechnologyObjectMethods.All, m => Assert.True(ProjectCapabilityCatalog.RequiresBoundProject(m)));
        Assert.All(TechnologyObjectMethods.All, m => Assert.True(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.LocalSession, m)));
        var errors = PlcOperationCatalog.ValidateAccessMode(new[] { Op(TechnologyObjectMethods.Export) }, McpAccessMode.ReadOnly);
        Assert.Contains("not permitted in read-only mode", Assert.Single(errors));
    }

    [Fact]
    public void RequiredAndUnknownFieldsAreEnforced()
    {
        Assert.Contains("missing required field(s): technologyType, version", WriteError(Op(TechnologyObjectMethods.Create)));
        Assert.Contains("'parameterName' is not valid for delete_technology_object", WriteError(Op(TechnologyObjectMethods.Delete, op => op.ParameterName = "x")));
        Assert.Contains("'overwrite' is not valid for update_technology_object_content",
            WriteError(Op(TechnologyObjectMethods.UpdateContent, op => { op.Content = "<x/>"; op.ExpectedContentHash = ContentHashRules.Compute("xml", "x"); op.Overwrite = true; })));
        Assert.Null(WriteError(Op(TechnologyObjectMethods.Create, op => { op.TechnologyType = "TO_PositioningAxis"; op.Version = "10.0"; op.FolderPath = "/Axes"; })));
        Assert.Contains("'version' '10' must be a version", WriteError(Op(TechnologyObjectMethods.Create, op => { op.TechnologyType = "TO_SpeedAxis"; op.Version = "10"; })));
        Assert.Contains("'name' must not contain '/'", WriteError(Op(TechnologyObjectMethods.Create, op => { op.Name = "Axes/X"; op.TechnologyType = "T"; op.Version = "1.0"; })));
        Assert.Contains("'newName' must not contain '/'", WriteError(Op(TechnologyObjectMethods.Rename, op => op.NewName = "a/b")));
        Assert.Contains("system group '/'", WriteError(Op(TechnologyObjectMethods.DeleteGroup, op => { op.Name = null; op.FolderPath = "/"; })));
        Assert.Contains("'source' format", WriteError(Op(TechnologyObjectMethods.UpdateContent, op => { op.Content = "<x/>"; op.ExpectedContentHash = ContentHashRules.Compute("source", "x"); })));
    }

    [Fact]
    public void ParameterReadsAndWritesValidateTheirShape()
    {
        Assert.Null(ReadError(Op(TechnologyObjectMethods.GetParameters, op => { op.Prefix = "Dynamics."; op.Offset = 200; op.Limit = 2000; })));
        Assert.Contains("cannot be combined with prefix", ReadError(Op(TechnologyObjectMethods.GetParameters, op => { op.ParameterName = "A.B"; op.Limit = 5; })));
        Assert.Contains("'limit' must be between 1 and 2000", ReadError(Op(TechnologyObjectMethods.GetParameters, op => op.Limit = 2001)));
        Assert.Contains("'offset' must be 0 or more", ReadError(Op(TechnologyObjectMethods.GetParameters, op => op.Offset = -1)));
        Assert.Contains("set exactly one of value", WriteError(Op(TechnologyObjectMethods.SetParameter, op => op.ParameterName = "A.B")));
        Assert.Contains("needs both tableName and tagName", WriteError(Op(TechnologyObjectMethods.SetParameter, op => { op.ParameterName = "A.B"; op.TagName = "T"; })));
        Assert.Null(WriteError(Op(TechnologyObjectMethods.SetParameter, op => { op.ParameterName = "A.B"; op.Value = string.Empty; })));
        Assert.Null(WriteError(Op(TechnologyObjectMethods.SetParameter, op => { op.ParameterName = "A.B"; op.TableName = "Tags"; op.TagName = "T"; })));
    }

    public static TheoryData<string, string, Action<PlcOperationRequest>> ValidConnections => new()
    {
        { "actor", "address", op => { op.InputAddress = 2048; op.OutputAddress = 2048; } },
        { "actor", "address", op => { op.InputAddress = 0; op.OutputAddress = 0; op.ConnectOption = "Default"; } },
        { "sensor", "module", op => { op.ModulePath = new[] { "Drive_1", "Module_1" }; op.InterfaceIndex = 1; } },
        { "torque", "modules", op => { op.ModulePath = new[] { "D", "In" }; op.OutputModulePath = new[] { "D", "Out" }; } },
        { "actor", "channel", op => { op.ModulePath = new[] { "D", "TM" }; op.ChannelNumber = 0; op.ChannelIoType = "Input"; } },
        { "actor", "tag", op => { op.TableName = "Tags"; op.TagName = "Speed"; } },
        { "torque", "dbMember", op => op.DbMemberPath = "\"Data\".Torque" },
        { "measuringInput", "address", op => op.InputAddress = 16 },
        { "measuringInput", "module", op => { op.ModulePath = new[] { "D", "TM" }; op.ChannelIndex = 2; } },
        { "outputCam", "address", op => op.OutputAddress = 24 },
        { "outputCam", "tag", op => { op.TableName = "Tags"; op.TagName = "Cam"; } },
        { "ident", "module", op => op.ModulePath = new[] { "Reader", "RF" } },
    };

    [Theory]
    [MemberData(nameof(ValidConnections))]
    public void DocumentedConnectOverloadsValidate(string connectionInterface, string kind, Action<PlcOperationRequest> fields)
        => Assert.Null(WriteError(Op(TechnologyObjectMethods.ConnectHardware, op =>
        {
            op.ConnectionInterface = connectionInterface;
            op.ConnectionKind = kind;
            fields(op);
        })));

    public static TheoryData<string, string, Action<PlcOperationRequest>, string> InvalidConnections => new()
    {
        { "torque", "tag", op => { op.TableName = "T"; op.TagName = "x"; }, "no documented Connect overload" },
        { "ident", "address", op => op.InputAddress = 1, "no documented Connect overload" },
        { "encoder", "address", _ => { }, "'connectionInterface' must be one of" },
        { "actor", "wire", _ => { }, "'connectionKind' must be one of" },
        { "actor", "address", op => op.InputAddress = 1, "requires 'outputAddress'" },
        { "measuringInput", "address", op => { op.InputAddress = 1; op.OutputAddress = 2; }, "'outputAddress' is not used" },
        { "measuringInput", "module", op => op.ModulePath = new[] { "D", "TM" }, "requires 'channelIndex'" },
        { "actor", "module", op => { op.ModulePath = new[] { "D", "M" }; op.ChannelIndex = 1; }, "'channelIndex' is not used" },
        { "actor", "module", op => op.ModulePath = new[] { "OnlyDevice" }, "at least one device item name" },
        { "actor", "modules", op => op.ModulePath = new[] { "D", "M" }, "requires 'outputModulePath'" },
        { "actor", "address", op => { op.InputAddress = -8; op.OutputAddress = 8; }, "'inputAddress' must be 0 or more" },
        { "actor", "address", op => { op.InputAddress = 8; op.OutputAddress = 8; op.InterfaceIndex = 0; }, "applies only to connectionInterface sensor" },
        { "actor", "dbMember", op => { op.DbMemberPath = "x"; op.ConnectOption = "Default"; }, "'connectOption' is not used" },
    };

    [Theory]
    [MemberData(nameof(InvalidConnections))]
    public void UndocumentedOrIncompleteConnectionsAreRefused(string connectionInterface, string kind, Action<PlcOperationRequest> fields, string expected)
        => Assert.Contains(expected, WriteError(Op(TechnologyObjectMethods.ConnectHardware, op =>
        {
            op.ConnectionInterface = connectionInterface;
            op.ConnectionKind = kind;
            fields(op);
        })));

    [Fact]
    public void DisconnectAndFilePathsValidate()
    {
        Assert.Contains("no documented Disconnect", WriteError(Op(TechnologyObjectMethods.DisconnectHardware, op => op.ConnectionInterface = "ident")));
        Assert.Null(WriteError(Op(TechnologyObjectMethods.DisconnectHardware, op => { op.ConnectionInterface = "sensor"; op.InterfaceIndex = 0; })));
        Assert.Contains("UNC", WriteError(Op(TechnologyObjectMethods.Export, op => op.FilePath = @"\\?\C:\x.xml")));
        Assert.Null(WriteError(Op(TechnologyObjectMethods.Import, op => { op.Name = null; op.FilePath = @"\\server\share\x.xml"; })));
        Assert.Contains("normalization", WriteError(Op(TechnologyObjectMethods.Import, op => { op.Name = null; op.FilePath = @"\\?\C:\x.xml"; })));
        Assert.Null(WriteError(Op(TechnologyObjectMethods.Import, op => { op.Name = null; op.FilePath = @"C:\Imports\x.xml"; op.Overwrite = true; op.FolderPath = "/Axes"; })));
    }

    [Fact]
    public void ToRequestForwardsEveryField()
    {
        var op = new PlcOperationRequest
        {
            OperationId = "x", Operation = TechnologyObjectMethods.ConnectHardware, PlcName = "PLC_1", Name = "Axis_1", ConnectionInterface = "actor",
            ConnectionKind = "modules", ModulePath = new[] { "D", "In" }, OutputModulePath = new[] { "D", "Out" }, ConnectOption = "Default", Overwrite = null,
        };
        var request = PlcTechnologyObjectRequests.ToRequest(op);
        Assert.Equal(new[] { "D", "In" }, request.ModulePath);
        Assert.Equal(new[] { "D", "Out" }, request.OutputModulePath);
        Assert.False(request.Overwrite);
        Assert.Equal("Default", request.ConnectOption);
        var json = JsonSerializer.Serialize(request);
        Assert.Contains("\"PlcName\":\"PLC_1\"", json);
    }
}
