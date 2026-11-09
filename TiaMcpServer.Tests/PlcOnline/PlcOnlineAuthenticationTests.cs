using System.Text.Json;
using Siemens.Engineering.Online.Configurations;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.PlcOnline;
using Xunit;

namespace TiaMcpServer.Tests.PlcOnline;

/// <summary>
/// Audit N2: the user-based PLC login (OnlineAuthenticationConfiguration / OnlineCredentials Name, Type, SetPassword) answered from
/// onlineUser and a password source, only over a secure channel unless allowInsecurePasswordTransfer. Not live-tested.
/// </summary>
public sealed class PlcOnlineAuthenticationTests
{
    private const string Key = PlcOnlineAuthentication.ConfigurationShortName;
    private static readonly PlcCallbackPasswordInfo[] Passwords = { new() { Type = Key, Variable = "PLC_USER_PASSWORD" } };

    private static PlcCallbackRecorder Recorder(PlcOnlineUserInfo? user, bool allowInsecure = false)
        => new(null, Passwords,
            new PlcCallbackReflectionAccess(PlcCallbackPolicy.OnlineConfigurationsNamespace, _ => null,
                name => name == "PLC_USER_PASSWORD" ? "s3cret" : null, onlineUser: user),
            allowInsecurePasswordTransfer: allowInsecure);

    [Fact]
    public void Login_SetsNameTypeAndPassword_AndPassesTheHostContract()
    {
        var recorder = Recorder(new PlcOnlineUserInfo { UserName = "Engineer", UserType = "ProjectUser" });
        var login = new OnlineAuthenticationConfiguration();

        recorder.Handle(login);

        Assert.Equal("Engineer", login.OnlineCredentials.Name);
        Assert.Equal(UserType.ProjectUser, login.OnlineCredentials.Type);
        Assert.Equal(6, login.OnlineCredentials.PasswordLength);
        var seen = Assert.Single(recorder.Seen);
        Assert.Equal(PlcCallbackPolicy.Kinds.Password, seen.Kind);
        Assert.Equal(Key, seen.DecisionKey);
        Assert.Equal(PlcCallbackPolicy.Decisions.PasswordSet, seen.Decision);
        Assert.Equal("PLC_USER_PASSWORD", seen.PasswordVariable);
        Assert.True(seen.SecureCommunication);
        Assert.DoesNotContain("s3cret", JsonSerializer.Serialize(recorder.Seen));
        PlcOnlinePayloadContract.ValidateCallbacks(recorder.Seen, recorder.Abort, Array.Empty<PlcCallbackDecisionInfo>(), Passwords);
        // The compares validate without the merged list: a valid source is enough.
        PlcOnlinePayloadContract.ValidateCallbacks(recorder.Seen, recorder.Abort, Array.Empty<PlcCallbackDecisionInfo>(), Array.Empty<PlcCallbackPasswordInfo>(),
            passwordsUnknown: true);
    }

    [Fact]
    public void Login_WithoutUserType_KeepsTiaPortalsType()
    {
        var login = new OnlineAuthenticationConfiguration();
        login.OnlineCredentials.Type = UserType.GlobalUser;
        Recorder(new PlcOnlineUserInfo { UserName = "Operator" }).Handle(login);
        Assert.Equal(UserType.GlobalUser, login.OnlineCredentials.Type);
        Assert.Equal("Operator", login.OnlineCredentials.Name);
    }

    [Fact]
    public void Login_WithoutOnlineUser_OrWithAnUnknownType_AbortsBeforeAnythingIsSet()
    {
        var missing = Recorder(null);
        var login = new OnlineAuthenticationConfiguration();
        Assert.Throws<PlcCallbackAbortedException>(() => missing.Handle(login));
        Assert.Equal(PlcCallbackPolicy.AbortReasons.DecisionNotApplicable, missing.Abort!.Reason);
        Assert.Contains("onlineUser", missing.Abort.Detail);
        Assert.Equal(string.Empty, login.OnlineCredentials.Name);
        Assert.Equal(0, login.OnlineCredentials.PasswordLength);
        PlcOnlinePayloadContract.ValidateCallbacks(missing.Seen, missing.Abort, Array.Empty<PlcCallbackDecisionInfo>(), Passwords);

        var unknown = Recorder(new PlcOnlineUserInfo { UserName = "Engineer", UserType = "Administrator" });
        var other = new OnlineAuthenticationConfiguration();
        Assert.Throws<PlcCallbackAbortedException>(() => unknown.Handle(other));
        Assert.Contains("ProjectUser", unknown.Abort!.Detail);
        Assert.Equal(0, other.OnlineCredentials.PasswordLength);
    }

    [Fact]
    public void Login_OverANonSecureChannel_IsNotSent_UnlessTheCallerAllowedIt()
    {
        var refused = Recorder(new PlcOnlineUserInfo { UserName = "Engineer" });
        var insecure = new OnlineAuthenticationConfiguration { IsSecureCommunication = false };
        Assert.Throws<PlcCallbackAbortedException>(() => refused.Handle(insecure));
        Assert.Equal(PlcCallbackPolicy.AbortReasons.InsecureCommunication, refused.Abort!.Reason);
        Assert.Equal(0, insecure.OnlineCredentials.PasswordLength);
        Assert.Equal(string.Empty, insecure.OnlineCredentials.Name);

        var allowed = Recorder(new PlcOnlineUserInfo { UserName = "Engineer" }, allowInsecure: true);
        var sent = new OnlineAuthenticationConfiguration { IsSecureCommunication = false };
        allowed.Handle(sent);
        Assert.Equal(6, sent.OnlineCredentials.PasswordLength);
        PlcOnlinePayloadContract.ValidateCallbacks(allowed.Seen, null, Array.Empty<PlcCallbackDecisionInfo>(), Passwords, allowInsecurePasswordTransfer: true);
    }

    [Fact]
    public void Download_LoginDuringTheConnection_FollowsTheSameRules()
    {
        var passwords = new[] { new PlcDownloadPasswordInfo { Type = Key, Variable = "PLC_USER_PASSWORD" } };
        var result = new PlcDownloadResultInfo();
        var recorder = new PlcDownloadCallbackRecorder(new PlcDownloadDecisionSet(null, passwords, true, "PLCSIM"), result,
            new PlcDownloadReflectionAccess(_ => null, name => name == "PLC_USER_PASSWORD" ? "s3cret" : null,
                onlineUser: new PlcOnlineUserInfo { UserName = "Engineer", UserType = "GlobalUser" }));
        var login = new OnlineAuthenticationConfiguration();

        recorder.OnlineLegitimation(login);

        Assert.Equal("Engineer", login.OnlineCredentials.Name);
        Assert.Equal(UserType.GlobalUser, login.OnlineCredentials.Type);
        var seen = Assert.Single(result.ConfigurationsSeen);
        Assert.Equal(PlcDownloadPolicy.Decisions.PasswordSet, seen.Decision);
        Assert.Equal(PlcDownloadPolicy.Kinds.Password, seen.Kind);
        Assert.True(seen.SecureCommunication);

        var withoutUser = new PlcDownloadResultInfo();
        var refused = new PlcDownloadCallbackRecorder(new PlcDownloadDecisionSet(null, passwords, true, "PLCSIM"), withoutUser,
            new PlcDownloadReflectionAccess(_ => null, name => name == "PLC_USER_PASSWORD" ? "s3cret" : null));
        Assert.Throws<PlcDownloadAbortedException>(() => refused.OnlineLegitimation(new OnlineAuthenticationConfiguration()));
        Assert.Equal(PlcDownloadPolicy.AbortReasons.DecisionNotApplicable, withoutUser.Abort!.Reason);
        Assert.Contains("onlineUser", withoutUser.Abort.Detail);
    }

    [Theory]
    [InlineData("", null, "userName")]
    [InlineData("a\nb", null, "control")]
    [InlineData("Engineer", "Project User", "userType")]
    [InlineData("Engineer", "ProjectUser", null)]
    public void Validate_RefusesBlankNamesAndNonIdentifierTypes(string name, string? type, string? error)
    {
        var message = PlcOnlineAuthentication.Validate(new PlcOnlineUserInfo { UserName = name, UserType = type });
        if (error is null) Assert.Null(message);
        else Assert.Contains(error, message);
    }
}

/// <summary>Audit N3: the Siemens-free projection of FingerprintDataResult and the host contract of get_station_fingerprint_data.</summary>
public sealed class PlcFingerprintDataTests
{
    private sealed class Item(object? identifier, object? value)
    {
        public object? FingerprintDataIdentifier { get; } = identifier;

        public object? FingerprintDataValue { get; } = value;
    }

    private sealed class Result(params Item[] items)
    {
        public List<Item> FingerprintDataItems { get; } = items.ToList();
    }

    private sealed class NoItems
    {
    }

    private static PlcStationFingerprintDataInfo Info(int max) => new()
    {
        ProviderOwner = "portal", Mode = "PN/IE", PcInterfaceName = "PLCSIM", PcInterfaceNumber = 1, TargetAddress = "10.0.0.5", MaxItems = max,
    };

    [Fact]
    public void Project_ProjectsUndocumentedValuesAsText_AndCapsTheList()
    {
        var info = Info(2);
        PlcFingerprintData.Project(new Result(new Item("A", new byte[] { 1, 2 }), new Item("B", null), new Item("C", 7)), 2, info, _ => true);
        Assert.Equal(3, info.ItemCount);
        Assert.True(info.Truncated);
        Assert.Equal("0102", info.Items[0].Value.Value);
        Assert.Equal(global::TiaMcpServer.Contracts.Shared.RuntimeValueStates.Null, info.Items[1].Value.State);
        var element = PlcOnlinePayloadContract.NormalizeFingerprintData(global::TiaMcpServer.Contracts.Json.WorkerJson.SerializePayload(info), "PN/IE", "PLCSIM", 1, "10.0.0.5", 2, null);
        Assert.Equal(2, element.GetProperty("items").GetArrayLength());
        // Another address than requested is a contract breach.
        Assert.ThrowsAny<JsonException>(() =>
            PlcOnlinePayloadContract.NormalizeFingerprintData(global::TiaMcpServer.Contracts.Json.WorkerJson.SerializePayload(info), "PN/IE", "PLCSIM", 1, "10.0.0.6", 2, null));
        var missing = Assert.Throws<global::TiaMcpServer.Contracts.Shared.RuntimeBindingException>(() => PlcFingerprintData.Project(new NoItems(), 5, Info(5), _ => true));
        Assert.Equal(global::TiaMcpServer.Contracts.Worker.WorkerFailureCategories.ApiMemberMissing, missing.Category);
    }

    [Fact]
    public void Catalog_RequiresModePcInterfaceAndAddress()
    {
        static TiaMcpServer.Plc.PlcOperationRequest Request(string address = "10.0.0.5", int? max = null) => new()
        {
            OperationId = "f", Operation = PlcOnlineMethods.GetStationFingerprintData, Mode = "PN/IE",
            PcInterface = new PlcOnlinePcInterfaceArgument { Name = "PLCSIM", Number = 1 }, TargetAddress = address, MaxElements = max,
        };
        var ok = TiaMcpServer.Plc.PlcOperationCatalog.ValidateRead(new[] { Request() });
        Assert.True(ok.IsValid, ok.Error);
        Assert.Contains("targetAddress", TiaMcpServer.Plc.PlcOperationCatalog.ValidateRead(new[] { Request(" x") }).Error);
        Assert.Contains("maxElements", TiaMcpServer.Plc.PlcOperationCatalog.ValidateRead(new[] { Request(max: 0) }).Error);
        Assert.Equal(global::TiaMcpServer.Contracts.Safety.OperationCapability.OnlineObserve,
            global::TiaMcpServer.Contracts.Safety.OperationPolicyCatalog.GetCapability(PlcOnlineMethods.GetStationFingerprintData));
    }
}
