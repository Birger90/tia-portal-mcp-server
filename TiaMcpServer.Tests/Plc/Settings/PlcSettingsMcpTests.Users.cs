using System.Text.Json;
using TiaMcpServer.Contracts.Plc.Settings;
using TiaMcpServer.Contracts.Safety;
using Xunit;

namespace TiaMcpServer.Tests.Plc.Settings;

/// <summary>Issue #13 (web server and OPC UA users) through plc_read/plc_write against the FakeWorker scenario plc-settings.</summary>
public sealed partial class PlcSettingsMcpTests
{
    private const string WebUserVariable = "TIA_MCP_TEST_WEB_USER_PASSWORD";
    private const string ShortUserVariable = "TIA_MCP_TEST_SHORT_USER_PASSWORD";
    private const string OpcUaUserVariable = "TIA_MCP_TEST_OPCUA_USER_PASSWORD";

    static partial void InitializeMore()
    {
        Environment.SetEnvironmentVariable(WebUserVariable, "FakeWebUser!1");
        Environment.SetEnvironmentVariable(ShortUserVariable, "short");
        Environment.SetEnvironmentVariable(OpcUaUserVariable, "FakeOpcUa!1");
        InitializeUmac();
    }

    static partial void InitializeUmac();

    [Fact]
    public async Task WebserverAndOpcUaUsers_AreCreatedChangedAndDeleted_WithPasswordsFromSourcesOnly()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);
        var (preview, _) = await session.WriteAsync(true,
            new { operationId = "web", operation = "create_webserver_user", userName = "operator", permissions = new[] { "ReadTag", "ReadTagStatus" }, passwordVariable = WebUserVariable });
        Assert.Equal(new[] { "plc_lowers_cpu_protection" }, GuardIds(preview));

        var (document, raw) = await session.WriteAsync(false,
            new { operationId = "web", operation = "create_webserver_user", userName = "operator", permissions = new[] { "ReadTag", "ReadTagStatus" }, passwordVariable = WebUserVariable },
            new { operationId = "perm", operation = "update_webserver_user", userName = "operator", permissions = new[] { "ReadTag" }, passwordVariable = WebUserVariable },
            new { operationId = "simple", operation = "update_simple_webserver_user", userName = "Everybody", settings = new Dictionary<string, object> { ["Active"] = true } },
            new { operationId = "opc", operation = "create_opcua_user", userName = "client1", passwordVariable = OpcUaUserVariable },
            new { operationId = "opcPw", operation = "set_opcua_user_password", userName = "client1", passwordVariable = OpcUaUserVariable });
        Assert.False(raw.IsError == true, document.GetRawText());
        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.Equal("readBack", Verification(document, "web").GetProperty("check").GetString());
        Assert.Equal("readBack", Verification(document, "perm").GetProperty("check").GetString());
        Assert.Equal("trusted", Verification(document, "opcPw").GetProperty("check").GetString());
        Assert.Equal("device item 'OPC UA'", Item(document, 3).GetProperty("result").GetProperty("owner").GetString());
        Assert.DoesNotContain("FakeWebUser!1", document.GetRawText());
        Assert.DoesNotContain("FakeOpcUa!1", document.GetRawText());

        var (read, _) = await session.ReadAsync(new { operationId = "s", operation = "get_cpu_settings" });
        var sections = Item(read, 0).GetProperty("result").GetProperty("plcs")[0].GetProperty("sections");
        var web = Section(sections, "webserverUsers").GetProperty("collections")[0].GetProperty("items")[0];
        Assert.Equal("operator", web.GetProperty("key").GetString());
        Assert.Equal("ReadTag", Property(web, "Permissions"));
        Assert.Equal("True", Property(Section(sections, "simpleWebserverUsers").GetProperty("collections")[0].GetProperty("items")[0], "Active"));
        Assert.Equal("client1", Section(sections, "opcUaUsers").GetProperty("collections")[0].GetProperty("items")[0].GetProperty("key").GetString());

        var (deleted, _) = await session.WriteAsync(false,
            new { operationId = "d1", operation = "delete_webserver_user", userName = "operator" },
            new { operationId = "d2", operation = "delete_opcua_user", userName = "client1" });
        Assert.True(deleted.GetProperty("success").GetBoolean(), deleted.GetRawText());
    }

    [Fact]
    public async Task APasswordThePolicyRefuses_FailsAsTiaPortalsRejection_AndNamesNoPassword()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);
        var (document, _) = await session.WriteAsync(false,
            new { operationId = "web", operation = "create_webserver_user", userName = "weak", permissions = new[] { "None" }, passwordVariable = ShortUserVariable });
        var item = Item(document, 0);
        Assert.Equal("failed", item.GetProperty("status").GetString());
        Assert.Equal("engineering_rejected", item.GetProperty("failure").GetProperty("category").GetString());
        Assert.DoesNotContain("\"short\"", document.GetRawText());
    }

    [Fact]
    public async Task CreateWebserverUser_WithoutPermissions_IsRefused()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);
        var (document, raw) = await session.WriteAsync(false,
            new { operationId = "web", operation = "create_webserver_user", userName = "x", passwordVariable = WebUserVariable });
        Assert.True(raw.IsError == true);
        Assert.Contains("needs permissions", document.GetProperty("error").GetProperty("message").GetString());
        Assert.DoesNotContain(session.Methods, PlcSettingsCatalog.IsWrite);
    }
}
