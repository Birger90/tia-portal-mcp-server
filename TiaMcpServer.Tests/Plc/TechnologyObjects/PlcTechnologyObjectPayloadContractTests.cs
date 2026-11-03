using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc.TechnologyObjects;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Plc;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Plc.TechnologyObjects;

/// <summary>Each technology object operation decodes exactly its one declared type; anything else is protocol_error and never echoed.</summary>
public sealed class PlcTechnologyObjectPayloadContractTests
{
    private static StructuredOperationItem Read(string operation, object payload, Action<PlcOperationRequest>? configure = null)
    {
        var op = new PlcOperationRequest { OperationId = "r", Operation = operation, Name = "Axis_1" };
        configure?.Invoke(op);
        return PlcPayloadContract.Project(op, WorkerCallResult.Ok(payload as string ?? WorkerJson.SerializePayload(payload)), _ => { });
    }

    private static StructuredOperationItem Write(string operation, TechnologyObjectWriteResultInfo payload, Action<PlcOperationRequest>? configure = null)
    {
        var op = new PlcOperationRequest { OperationId = "w", Operation = operation, Name = "Axis_1" };
        configure?.Invoke(op);
        return PlcPayloadContract.ProjectWrite(op, WorkerCallResult.Ok(WorkerJson.SerializePayload(payload)), _ => { });
    }

    /// <summary>An interface reporting every documented property once: the named ones as given, the rest null (or unreadable).</summary>
    private static TechnologyObjectConnectionInterfaceInfo Full(string connectionInterface, bool? connected, int? index = null, string[]? unreadable = null,
        params TechnologyObjectConnectionPropertyInfo[] properties)
    {
        var endpoint = new TechnologyObjectConnectionInterfaceInfo { Interface = connectionInterface, Provider = "P", Index = index, IsConnected = connected };
        foreach (var name in TechnologyObjectPolicy.PropertiesFor(connectionInterface))
        {
            if (unreadable?.Contains(name) == true) endpoint.Unreadable.Add(name);
            else endpoint.Properties.Add(properties.FirstOrDefault(p => p.Name == name)
                ?? (name == "IsConnected" ? new() { Name = name, Kind = "boolean", Value = connected == true ? "true" : "false" } : new() { Name = name, Kind = "null" }));
        }

        return endpoint;
    }

    private static TechnologyObjectParameterInfo Parameter(string name = "Dynamics.Velocity") => new() { Name = name, ValueKind = "real", ValueType = "System.Double", Value = "1" };

    [Fact]
    public void InventoryParametersAndConnectionsDecode()
    {
        var list = Read(TechnologyObjectMethods.List, new TechnologyObjectInventoryInfo
        {
            Plcs = { new TechnologyObjectPlcInfo { PlcName = "PLC_1", DeviceName = "S", Groups = { new TechnologyObjectGroupInfo { Path = "/Axes", Name = "Axes" } },
                TechnologyObjects = { new TechnologyObjectInfo { Name = "Axis_1", GroupPath = "/" } } } },
        });
        Assert.Equal(OperationBatchStatus.Succeeded, list.Status);

        var page = Read(TechnologyObjectMethods.GetParameters, new TechnologyObjectParametersInfo
        {
            PlcName = "PLC_1", Name = "AXIS_1", GroupPath = "/", TotalCount = 3, MatchedCount = 3, Limit = 2, NextOffset = 2, Parameters = { Parameter(), Parameter("A") },
        }, op => op.Limit = 2);
        Assert.Equal(OperationBatchStatus.Succeeded, page.Status);

        var connection = Read(TechnologyObjectMethods.GetHardwareConnection, new TechnologyObjectHardwareConnectionInfo
        {
            PlcName = "PLC_1", Name = "Axis_1", GroupPath = "/",
            Interfaces = { Full("actor", false, properties: new TechnologyObjectConnectionPropertyInfo { Name = "InputModule", Kind = "deviceItem", ModulePath = new() { "D", "M" } }) },
        });
        Assert.Equal(OperationBatchStatus.Succeeded, connection.Status);

        var content = Read(TechnologyObjectMethods.GetContent, "<Document />");
        Assert.Equal("xml", content.Result!.Value.GetProperty("format").GetString());
        Assert.StartsWith("xml:sha256:", content.Result.Value.GetProperty("contentHash").GetString());
    }

    public static TheoryData<string, string> MalformedReads => new()
    {
        { TechnologyObjectMethods.List, """{"plcs":null}""" },
        { TechnologyObjectMethods.List, """{"plcs":[{"plcName":"PLC_1","deviceName":"S","groups":[{"path":"/","name":""}],"technologyObjects":[],"complete":true}]}""" },
        { TechnologyObjectMethods.List, """{"plcs":[{"plcName":"PLC_1","deviceName":"S","groups":[],"technologyObjects":[],"complete":true}],"extra":1}""" },
        { TechnologyObjectMethods.GetParameters, """{"plcName":"PLC_1","deviceName":"S","name":"Other","groupPath":"/","parameterName":null,"prefix":null,"totalCount":0,"matchedCount":0,"offset":0,"limit":1,"nextOffset":null,"parameters":[]}""" },
        { TechnologyObjectMethods.GetParameters, """{"plcName":"PLC_1","deviceName":"S","name":"Axis_1","groupPath":"/","parameterName":null,"prefix":null,"totalCount":1,"matchedCount":1,"offset":0,"limit":1,"nextOffset":null,"parameters":[{"name":"A","valueKind":"weird","valueType":null,"value":null,"enumMembers":[]}]}""" },
        { TechnologyObjectMethods.GetHardwareConnection, """{"plcName":"PLC_1","deviceName":"S","name":"Axis_1","groupPath":"/","services":[],"interfaces":[{"interface":"encoder","provider":"P","index":null,"runtimeType":null,"isConnected":null,"properties":[],"unreadable":[]}]}""" },
    };

    [Theory]
    [MemberData(nameof(MalformedReads))]
    public void MalformedReadsAreProtocolErrorsWithoutEcho(string operation, string payload)
    {
        var item = Read(operation, payload);
        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
        Assert.DoesNotContain("encoder", item.Failure.Message);
    }

    [Fact]
    public void WriteResultsMustMatchTheRequest()
    {
        Assert.Equal(OperationBatchStatus.Succeeded, Write(TechnologyObjectMethods.SetParameter,
            new() { Operation = TechnologyObjectMethods.SetParameter, PlcName = "PLC_1", Name = "Axis_1", GroupPath = "/", Parameter = Parameter(), PreviousParameter = Parameter() },
            op => op.ParameterName = "Dynamics.Velocity").Status);
        Assert.Equal(WorkerFailureCategories.ProtocolError, Write(TechnologyObjectMethods.SetParameter,
            new() { Operation = TechnologyObjectMethods.SetParameter, PlcName = "PLC_1", Name = "Axis_1", Parameter = Parameter("Other"), PreviousParameter = Parameter() },
            op => op.ParameterName = "Dynamics.Velocity").Failure!.Category);
        Assert.Equal(WorkerFailureCategories.ProtocolError, Write(TechnologyObjectMethods.Rename,
            new() { Operation = TechnologyObjectMethods.Rename, PlcName = "PLC_1", Name = "Axis_1", TechnologyObject = new() { Name = "Axis_1" } },
            op => op.NewName = "Spindle").Failure!.Category);
        Assert.Equal(WorkerFailureCategories.ProtocolError, Write(TechnologyObjectMethods.Export,
            new() { Operation = TechnologyObjectMethods.Export, PlcName = "PLC_1", Name = "Axis_1", FilePath = @"C:\x.xml", FileBytes = 3, FileSha256 = "abc" }).Failure!.Category);
        Assert.Equal(WorkerFailureCategories.ProtocolError, Write(TechnologyObjectMethods.Delete,
            new() { Operation = TechnologyObjectMethods.Create, PlcName = "PLC_1", Name = "Axis_1" }).Failure!.Category);
        Assert.Equal(OperationBatchStatus.Succeeded, Write(TechnologyObjectMethods.ConnectHardware,
            new()
            {
                Operation = TechnologyObjectMethods.ConnectHardware, PlcName = "PLC_1", Name = "Axis_1",
                PreviousInterface = Full("actor", false),
                Interface = Full("actor", true),
            },
            op => op.ConnectionInterface = "actor").Status);
        Assert.Equal(WorkerFailureCategories.ProtocolError, Write(TechnologyObjectMethods.ConnectHardware,
            new()
            {
                Operation = TechnologyObjectMethods.ConnectHardware, PlcName = "PLC_1", Name = "Axis_1",
                PreviousInterface = Full("torque", false), Interface = Full("actor", true),
            },
            op => op.ConnectionInterface = "actor").Failure!.Category);
        // Before and after must be the same endpoint (position included).
        Assert.Equal(WorkerFailureCategories.ProtocolError, Write(TechnologyObjectMethods.ConnectHardware,
            new()
            {
                Operation = TechnologyObjectMethods.ConnectHardware, PlcName = "PLC_1", Name = "Axis_1",
                PreviousInterface = Full("sensor", false, 0), Interface = Full("sensor", true, 1),
            },
            op => op.ConnectionInterface = "sensor").Failure!.Category);
        Assert.Equal(WorkerFailureCategories.ProtocolError, Write(TechnologyObjectMethods.Import,
            new() { Operation = TechnologyObjectMethods.Import, PlcName = "PLC_1", GroupPath = "/", ImportedObjects = { "Axis_3", "axis_3" } }).Failure!.Category);
        Assert.Equal(WorkerFailureCategories.ProtocolError, Write(TechnologyObjectMethods.UpdateContent,
            new() { Operation = TechnologyObjectMethods.UpdateContent, PlcName = "PLC_1", Name = "Axis_1", GroupPath = "/", TechnologyObject = new() { Name = "Axis_1" } }).Failure!.Category);
    }

    private static TechnologyObjectWriteResultInfo Exported(string path, string hash) => new()
    {
        Operation = TechnologyObjectMethods.Export, PlcName = "PLC_1", Name = "Axis_1", GroupPath = "/", FilePath = path, FileBytes = 3, FileSha256 = hash,
    };

    [Fact]
    public void ExportResultsNameTheRequestedFileAndALowerCaseSha256()
    {
        var hash = new string('a', 64);
        Assert.Equal(OperationBatchStatus.Succeeded, Write(TechnologyObjectMethods.Export, Exported(@"C:\Exports\Axis_1.xml", hash), op => op.FilePath = "C:/Exports/Axis_1.xml").Status);
        Assert.Equal(OperationBatchStatus.Succeeded, Write(TechnologyObjectMethods.Export, Exported(@"c:\exports\AXIS_1.xml", hash), op => op.FilePath = @"C:\Exports\Axis_1.xml").Status);
        Assert.Equal(WorkerFailureCategories.ProtocolError,
            Write(TechnologyObjectMethods.Export, Exported(@"C:\Exports\Other.xml", hash), op => op.FilePath = @"C:\Exports\Axis_1.xml").Failure!.Category);
        Assert.Equal(WorkerFailureCategories.ProtocolError,
            Write(TechnologyObjectMethods.Export, Exported(@"C:\Exports\Axis_1.xml", new string('A', 64)), op => op.FilePath = @"C:\Exports\Axis_1.xml").Failure!.Category);
        Assert.Equal(WorkerFailureCategories.ProtocolError,
            Write(TechnologyObjectMethods.Export, Exported(@"C:\Exports\Axis_1.xml", new string('g', 64)), op => op.FilePath = @"C:\Exports\Axis_1.xml").Failure!.Category);
    }

    [Fact]
    public void PayloadBoundsAreEnforced()
    {
        TechnologyObjectHardwareConnectionInfo Connection() => new() { PlcName = "PLC_1", Name = "Axis_1", GroupPath = "/" };
        var services = Connection();
        services.Services.AddRange(Enumerable.Range(0, TechnologyObjectPolicy.MaxServices + 1).Select(i => "S" + i));
        Assert.Equal(WorkerFailureCategories.ProtocolError, Read(TechnologyObjectMethods.GetHardwareConnection, services).Failure!.Category);

        var undocumented = Connection();
        undocumented.Interfaces.Add(new() { Interface = "ident", Provider = "P", Properties = { new() { Name = "IsConnected", Kind = "boolean", Value = "true" } } });
        Assert.Equal(WorkerFailureCategories.ProtocolError, Read(TechnologyObjectMethods.GetHardwareConnection, undocumented).Failure!.Category);

        // Every documented property exactly once, read or unreadable; no duplicate interfaces; at most MaxInterfaces.
        var complete = Connection();
        complete.Interfaces.Add(Full("sensor", false, 0, new[] { "OutputTag" }));
        complete.Interfaces.Add(Full("sensor", false, 1));
        Assert.Equal(OperationBatchStatus.Succeeded, Read(TechnologyObjectMethods.GetHardwareConnection, complete).Status);
        var missing = Connection();
        missing.Interfaces.Add(Full("torque", false));
        missing.Interfaces[0].Properties.RemoveAt(0);
        Assert.Equal(WorkerFailureCategories.ProtocolError, Read(TechnologyObjectMethods.GetHardwareConnection, missing).Failure!.Category);
        var both = Connection();
        both.Interfaces.Add(Full("torque", false));
        both.Interfaces[0].Unreadable.Add("IsConnected");
        Assert.Equal(WorkerFailureCategories.ProtocolError, Read(TechnologyObjectMethods.GetHardwareConnection, both).Failure!.Category);
        var twice = Connection();
        twice.Interfaces.Add(Full("sensor", false, 0));
        twice.Interfaces.Add(Full("sensor", false, 0));
        Assert.Equal(WorkerFailureCategories.ProtocolError, Read(TechnologyObjectMethods.GetHardwareConnection, twice).Failure!.Category);
        var crowdedInterfaces = Connection();
        crowdedInterfaces.Interfaces.AddRange(Enumerable.Range(0, TechnologyObjectPolicy.MaxInterfaces + 1).Select(i => Full("sensor", false, i)));
        Assert.Equal(WorkerFailureCategories.ProtocolError, Read(TechnologyObjectMethods.GetHardwareConnection, crowdedInterfaces).Failure!.Category);
        var deep = Connection();
        deep.Interfaces.Add(Full("actor", true, properties: new TechnologyObjectConnectionPropertyInfo
        {
            Name = "InputModule", Kind = "deviceItem", ModulePath = Enumerable.Range(0, TechnologyObjectPolicy.MaxModulePathDepth + 1).Select(i => "D" + i).ToList(),
        }));
        Assert.Equal(WorkerFailureCategories.ProtocolError, Read(TechnologyObjectMethods.GetHardwareConnection, deep).Failure!.Category);
        var long_ = Connection();
        long_.Interfaces.Add(Full("actor", true, properties: new TechnologyObjectConnectionPropertyInfo { Name = "PathToDBMember", Kind = "string", Value = new string('x', TechnologyObjectPolicy.MaxTextChars + 13) }));
        Assert.Equal(WorkerFailureCategories.ProtocolError, Read(TechnologyObjectMethods.GetHardwareConnection, long_).Failure!.Category);

        // The inventory never exceeds MaxObjects (objects plus groups); groups are unique.
        var crowded = new TechnologyObjectInventoryInfo { Plcs = { new TechnologyObjectPlcInfo { PlcName = "PLC_1", DeviceName = "S" } } };
        crowded.Plcs[0].TechnologyObjects.AddRange(Enumerable.Range(0, TechnologyObjectPolicy.MaxObjects + 1).Select(i => new TechnologyObjectInfo { Name = "A" + i, GroupPath = "/" }));
        Assert.Equal(WorkerFailureCategories.ProtocolError, Read(TechnologyObjectMethods.List, crowded).Failure!.Category);
        var groups = new TechnologyObjectInventoryInfo { Plcs = { new TechnologyObjectPlcInfo { PlcName = "PLC_1", DeviceName = "S",
            Groups = { new TechnologyObjectGroupInfo { Path = "/A", Name = "A" }, new TechnologyObjectGroupInfo { Path = "/a", Name = "a" } } } } };
        Assert.Equal(WorkerFailureCategories.ProtocolError, Read(TechnologyObjectMethods.List, groups).Failure!.Category);

        TechnologyObjectParametersInfo Page(TechnologyObjectParameterInfo parameter, int offset = 0, int limit = 200) => new()
        {
            PlcName = "PLC_1", Name = "Axis_1", GroupPath = "/", TotalCount = 1, MatchedCount = 1, Offset = offset, Limit = limit, Parameters = { parameter },
        };
        var many = new TechnologyObjectParameterInfo { Name = "E", ValueKind = "enum", Value = "A" };
        many.EnumMembers.AddRange(Enumerable.Range(0, TechnologyObjectPolicy.MaxEnumMembers + 1).Select(i => "M" + i));
        Assert.Equal(WorkerFailureCategories.ProtocolError, Read(TechnologyObjectMethods.GetParameters, Page(many)).Failure!.Category);
        var truncated = new TechnologyObjectParameterInfo { Name = "E", ValueKind = "enum", Value = "A", EnumMembersTruncated = true };
        truncated.EnumMembers.AddRange(Enumerable.Range(0, TechnologyObjectPolicy.MaxEnumMembers).Select(i => "M" + i));
        Assert.Equal(OperationBatchStatus.Succeeded, Read(TechnologyObjectMethods.GetParameters, Page(truncated)).Status);
        Assert.Equal(WorkerFailureCategories.ProtocolError,
            Read(TechnologyObjectMethods.GetParameters, Page(new() { Name = "R", ValueKind = "real", Value = "1", EnumFlags = true })).Failure!.Category);

        // Paging must answer the request.
        Assert.Equal(WorkerFailureCategories.ProtocolError, Read(TechnologyObjectMethods.GetParameters, Page(Parameter(), offset: 5)).Failure!.Category);
        Assert.Equal(OperationBatchStatus.Succeeded, Read(TechnologyObjectMethods.GetParameters, Page(Parameter(), limit: 7), op => op.Limit = 7).Status);
        Assert.Equal(WorkerFailureCategories.ProtocolError, Read(TechnologyObjectMethods.GetParameters, Page(Parameter("Other")), op => op.Prefix = "Dynamics.").Failure!.Category);
        var single = Page(Parameter());
        single.TotalCount = null;
        Assert.Equal(WorkerFailureCategories.ProtocolError, Read(TechnologyObjectMethods.GetParameters, single, op => op.ParameterName = "Dynamics.Acceleration").Failure!.Category);
    }

    [Fact]
    public void AFailedConnectCarriesItsBeforeAndAfterStateAsTheItemResult()
    {
        var op = new PlcOperationRequest { OperationId = "c", Operation = TechnologyObjectMethods.ConnectHardware, Name = "Axis_1", ConnectionInterface = "actor" };
        var evidence = WorkerJson.SerializePayload(new TechnologyObjectWriteResultInfo
        {
            Operation = TechnologyObjectMethods.ConnectHardware, PlcName = "PLC_1", Name = "Axis_1", GroupPath = "/", PreviousInterface = Full("actor", false), Interface = Full("actor", false),
        });
        var failed = new WorkerCallResult(false, string.Empty, "connect not evidenced", WorkerFailureCategories.PostconditionFailed, Array.Empty<string>()) { PlcDownloadOutcome = evidence };
        var item = PlcPayloadContract.ProjectWrite(op, failed, _ => { });
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, item.Failure!.Category);
        Assert.False(item.Result!.Value.GetProperty("previousInterface").GetProperty("isConnected").GetBoolean());

        // Evidence that contradicts the request is rejected, never echoed.
        var wrong = new WorkerCallResult(false, string.Empty, "connect not evidenced", WorkerFailureCategories.PostconditionFailed, Array.Empty<string>())
        {
            PlcDownloadOutcome = evidence.Replace("\"actor\"", "\"torque\"", StringComparison.Ordinal),
        };
        var rejected = PlcPayloadContract.ProjectWrite(op, wrong, _ => { });
        Assert.Null(rejected.Result);
        Assert.Contains("did not match its declared contract", rejected.Failure!.Message);
    }
}
