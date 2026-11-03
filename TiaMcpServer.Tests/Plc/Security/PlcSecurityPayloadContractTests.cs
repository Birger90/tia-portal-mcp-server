using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc.Security;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Plc;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Plc.Security;

/// <summary>The declared result types of the CPU protection operations: typed JSON, or protocol_error without echoing the payload.</summary>
public sealed class PlcSecurityPayloadContractTests
{
    private static PlcOperationRequest Op(string operation, string? accessLevel = null, string? plcName = null)
        => new() { OperationId = "a", Operation = operation, AccessLevel = accessLevel, PlcName = plcName };

    private static PlcSecuritySettingInfo Setting(string service, string property, bool offered = true, bool readable = false, string? value = null) => new()
    {
        Service = service, Property = property, ServiceOffered = offered, ServiceType = offered ? service : null,
        Readable = readable, Value = value, Writable = offered, AvailableValues = offered ? PlcSecurityPolicy.DocumentedAccessLevels.ToList() : new(),
        Note = readable ? null : PlcSecurityPolicy.NoPasswordReadNote,
    };

    private static PlcSecurityReadInfo Read() => new()
    {
        Plcs =
        {
            new PlcSecurityPlcInfo
            {
                PlcName = "PLC_1", DeviceName = "Station_1",
                Services = { PlcSecurityPolicy.AccessLevelProviderType, "Siemens.Engineering.HW.Features.PlcMasterSecretConfigurator" },
                AccessLevel = Setting(PlcSecurityPolicy.AccessLevelProviderType, PlcSecurityPolicy.AccessLevelProperty),
                AccessControl = Setting(PlcSecurityPolicy.AccessControlProviderType, PlcSecurityPolicy.AccessControlProperty, offered: false),
                ConfidentialDataProtection = new PlcConfidentialDataInfo
                {
                    ServiceOffered = true, ServiceType = "Siemens.Engineering.HW.Features.PlcMasterSecretConfigurator",
                    ProtectBindable = true, UnprotectBindable = false, UnprotectCandidates = { "Boolean Unprotect()" },
                    ObservedMembers = { new PlcSecurityMemberInfo { Name = "IsProtected", Type = "System.Boolean", Readable = true, Value = "True" } },
                    Note = "No documented member reports whether the protection is on.",
                },
            },
        },
    };

    private static PlcSecurityWriteResultInfo LevelResult(string readBack = PlcSecurityPolicy.ReadBacks.Matched, string? after = "FullAccess") => new()
    {
        Operation = PlcSecurityMethods.SetAccessLevel, PlcName = "PLC_1", DeviceName = "Station_1", Setting = PlcSecurityPolicy.Settings.AccessLevel,
        ServiceType = PlcSecurityPolicy.AccessLevelProviderType, Member = "PlcProtectionAccessLevel { set; }", Requested = "FullAccess",
        Before = null, BeforeReadable = false, After = after, AfterReadable = readBack == PlcSecurityPolicy.ReadBacks.Matched, ReadBack = readBack,
        Calls = { "PlcProtectionAccessLevel { set; } [FullAccess]" },
    };

    private static PlcSecurityWriteResultInfo ConfidentialResult() => new()
    {
        Operation = PlcSecurityMethods.DisableConfidentialDataProtection, PlcName = "PLC_1", DeviceName = "Station_1",
        Setting = PlcSecurityPolicy.Settings.ConfidentialDataProtection, ServiceType = "Siemens.Engineering.HW.Features.PlcMasterSecretConfigurator",
        Member = "void Unprotect()", ReadBack = PlcSecurityPolicy.ReadBacks.NotAvailable, Calls = { "void Unprotect()" },
    };

    private static StructuredOperationItem ProjectRead(PlcOperationRequest op, object payload)
        => PlcPayloadContract.Project(op, WorkerCallResult.Ok(WorkerJson.SerializePayload(payload)));

    private static StructuredOperationItem ProjectWrite(PlcOperationRequest op, object payload)
        => PlcPayloadContract.ProjectWrite(op, WorkerCallResult.Ok(WorkerJson.SerializePayload(payload)));

    [Fact]
    public void Read_ProjectsTheTypedResult()
    {
        var item = ProjectRead(Op(PlcSecurityMethods.GetSecurity), Read());

        Assert.Equal(OperationBatchStatus.Succeeded, item.Status);
        var plc = item.Result!.Value.GetProperty("plcs")[0];
        Assert.False(plc.GetProperty("accessLevel").GetProperty("readable").GetBoolean());
        Assert.Equal(PlcSecurityPolicy.NoPasswordReadNote, plc.GetProperty("accessLevel").GetProperty("note").GetString());
        Assert.False(plc.GetProperty("confidentialDataProtection").GetProperty("unprotectBindable").GetBoolean());
    }

    public static IEnumerable<object[]> MalformedReads()
    {
        yield return new object[] { "valueWithoutReadable", new Action<PlcSecurityReadInfo>(r => r.Plcs[0].AccessLevel.Value = "PRIVATE") };
        yield return new object[] { "wrongService", new Action<PlcSecurityReadInfo>(r => r.Plcs[0].AccessLevel.Service = "PRIVATE") };
        yield return new object[] { "offeredWithoutType", new Action<PlcSecurityReadInfo>(r => r.Plcs[0].AccessControl.ServiceOffered = true) };
        yield return new object[] { "notOfferedButBindable", new Action<PlcSecurityReadInfo>(r =>
        {
            r.Plcs[0].ConfidentialDataProtection.ServiceOffered = false;
            r.Plcs[0].ConfidentialDataProtection.ServiceType = null;
        }) };
        yield return new object[] { "noPlcs", new Action<PlcSecurityReadInfo>(r => r.Plcs.Clear()) };
        yield return new object[] { "tooManyServices", new Action<PlcSecurityReadInfo>(r =>
            r.Plcs[0].Services = Enumerable.Range(0, PlcSecurityPolicy.MaxServices + 1).Select(i => "PRIVATE" + i).ToList()) };
    }

    [Theory]
    [MemberData(nameof(MalformedReads))]
    public void Read_ContradictionsAreProtocolErrorsWithoutEcho(string name, Action<PlcSecurityReadInfo> damage)
    {
        var payload = Read();
        damage(payload);
        var item = ProjectRead(Op(PlcSecurityMethods.GetSecurity), payload);

        Assert.True(item.Status == OperationBatchStatus.Failed, name);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
        Assert.DoesNotContain("PRIVATE", item.Failure.Message);
    }

    [Fact]
    public void Read_APlcTheRequestDidNotNameIsRejected()
    {
        Assert.Equal(OperationBatchStatus.Succeeded, ProjectRead(Op(PlcSecurityMethods.GetSecurity, plcName: "station_1"), Read()).Status);
        Assert.Equal(WorkerFailureCategories.ProtocolError, ProjectRead(Op(PlcSecurityMethods.GetSecurity, plcName: "PLC_2"), Read()).Failure!.Category);
    }

    [Fact]
    public void Read_AMissingMemberIsAProtocolError()
    {
        var item = PlcPayloadContract.Project(Op(PlcSecurityMethods.GetSecurity),
            WorkerCallResult.Ok("""{"plcs":[{"plcName":"PLC_1","deviceName":"Station_1"}]}"""));
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
    }

    [Theory]
    [InlineData(PlcSecurityPolicy.ReadBacks.Matched, "FullAccess")]
    [InlineData(PlcSecurityPolicy.ReadBacks.Unreadable, null)]
    public void LevelWrite_MatchedOrUnreadable_IsAccepted(string readBack, string? after)
    {
        var item = ProjectWrite(Op(PlcSecurityMethods.SetAccessLevel, "FullAccess"), LevelResult(readBack, after));
        Assert.Equal(OperationBatchStatus.Succeeded, item.Status);
        Assert.Equal(readBack, item.Result!.Value.GetProperty("readBack").GetString());
    }

    public static IEnumerable<object[]> MalformedLevelWrites()
    {
        yield return new object[] { "otherRequested", new Action<PlcSecurityWriteResultInfo>(r => r.Requested = "NoAccess") };
        yield return new object[] { "readBackDiffers", new Action<PlcSecurityWriteResultInfo>(r => r.After = "NoAccess") };
        yield return new object[] { "unreadableWithValue", new Action<PlcSecurityWriteResultInfo>(r => { r.ReadBack = PlcSecurityPolicy.ReadBacks.Unreadable; r.AfterReadable = false; }) };
        yield return new object[] { "notAvailable", new Action<PlcSecurityWriteResultInfo>(r => r.ReadBack = PlcSecurityPolicy.ReadBacks.NotAvailable) };
        yield return new object[] { "unknownReadBack", new Action<PlcSecurityWriteResultInfo>(r => r.ReadBack = "PRIVATE") };
        yield return new object[] { "otherOperation", new Action<PlcSecurityWriteResultInfo>(r => r.Operation = PlcSecurityMethods.DisableConfidentialDataProtection) };
        yield return new object[] { "otherSetting", new Action<PlcSecurityWriteResultInfo>(r => r.Setting = PlcSecurityPolicy.Settings.ConfidentialDataProtection) };
        yield return new object[] { "beforeWithoutReadable", new Action<PlcSecurityWriteResultInfo>(r => r.Before = "NoAccess") };
    }

    [Theory]
    [MemberData(nameof(MalformedLevelWrites))]
    public void LevelWrite_ContradictionsAreProtocolErrors(string name, Action<PlcSecurityWriteResultInfo> damage)
    {
        var payload = LevelResult();
        damage(payload);
        var item = ProjectWrite(Op(PlcSecurityMethods.SetAccessLevel, "FullAccess"), payload);
        Assert.True(item.Status == OperationBatchStatus.Failed, name);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
        Assert.DoesNotContain("PRIVATE", item.Failure.Message);
    }

    [Fact]
    public void ConfidentialWrite_IsAcceptedOnlyWithoutLevelValuesAndWithNotAvailable()
    {
        var op = Op(PlcSecurityMethods.DisableConfidentialDataProtection);
        Assert.Equal(OperationBatchStatus.Succeeded, ProjectWrite(op, ConfidentialResult()).Status);

        var claimed = ConfidentialResult();
        claimed.ReadBack = PlcSecurityPolicy.ReadBacks.Matched;
        Assert.Equal(WorkerFailureCategories.ProtocolError, ProjectWrite(op, claimed).Failure!.Category);

        var level = ConfidentialResult();
        level.Requested = "FullAccess";
        Assert.Equal(WorkerFailureCategories.ProtocolError, ProjectWrite(op, level).Failure!.Category);

        var noCalls = ConfidentialResult();
        noCalls.Calls.Clear();
        Assert.Equal(WorkerFailureCategories.ProtocolError, ProjectWrite(op, noCalls).Failure!.Category);
    }

    [Fact]
    public void PasswordSources_ComeBackOnlyAsTheRequestsTokens()
    {
        var op = Op(PlcSecurityMethods.DisableConfidentialDataProtection);
        op.PasswordVariable = "SECRET_SOURCE";
        var used = ConfidentialResult();
        used.Member = "void Unprotect(SecureString)";
        used.Calls = new() { used.Member };
        used.PasswordSources = new() { new PlcSecurityPasswordUseInfo { Purpose = PlcSecurityPolicy.MasterSecretPurpose, Source = "SECRET_SOURCE", Kind = "environment" } };
        Assert.Equal(OperationBatchStatus.Succeeded, ProjectWrite(op, used).Status);

        // A source the request did not give (or something that is no source, e.g. a value) is rejected and never echoed.
        used.PasswordSources[0].Source = "PRIVATE value!";
        var item = ProjectWrite(op, used);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
        Assert.DoesNotContain("PRIVATE", item.Failure.Message);
    }
}
