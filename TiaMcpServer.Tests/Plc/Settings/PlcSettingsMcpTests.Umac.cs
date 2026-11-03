using TiaMcpServer.Contracts.Plc.Settings;
using TiaMcpServer.Contracts.Safety;
using Xunit;

namespace TiaMcpServer.Tests.Plc.Settings;

/// <summary>Issue #12 (UMAC) through plc_read/plc_write against the FakeWorker scenario plc-settings.</summary>
public sealed partial class PlcSettingsMcpTests
{
    private const string ProjectUserVariable = "TIA_MCP_TEST_PROJECT_USER_PASSWORD";

    static partial void InitializeUmac()
    {
        Environment.SetEnvironmentVariable(ProjectUserVariable, "FakeProjectUser!1");
        InitializeCertificates();
    }

    static partial void InitializeCertificates();

    [Fact]
    public async Task Umac_RolesUsersAndRights_AreChangedAndReadBack()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);
        var (preview, _) = await session.WriteAsync(true,
            new { operationId = "anon", operation = "set_anonymous_user", active = true });
        Assert.Equal(new[] { "plc_changes_project_access", "plc_lowers_cpu_protection" }, GuardIds(preview));

        var (document, raw) = await session.WriteAsync(false,
            new { operationId = "role", operation = "create_custom_role", name = "Operator", settings = new Dictionary<string, object> { ["Comment"] = "Line operators", ["SessionTimeOut"] = 15 } },
            new { operationId = "eng", operation = "assign_engineering_right", roleName = "Operator", rightName = "Open project read-only" },
            new { operationId = "dev", operation = "assign_device_function_right", roleName = "Operator", rightName = "Read access" },
            new { operationId = "user", operation = "create_project_user", userName = "anna", passwordVariable = ProjectUserVariable },
            new { operationId = "assign", operation = "assign_user_role", userName = "anna", roleName = "Operator" },
            new { operationId = "update", operation = "update_project_user", userName = "anna", aliasName = "Anna", active = false, runtimeSessionTimeout = true },
            new { operationId = "umc", operation = "update_umc_user", userName = "DOMAIN\\engineer", active = false },
            new { operationId = "policy", operation = "set_umac_password_policy", settings = new Dictionary<string, object> { ["MinimumLength"] = 10 } },
            new { operationId = "right", operation = "create_custom_device_function_right", name = "Open valve", group = "Process" });
        Assert.False(raw.IsError == true, document.GetRawText());
        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        foreach (var id in new[] { "role", "eng", "dev", "user", "assign", "update", "umc", "policy", "right" })
            Assert.True(Verification(document, id).GetProperty("success").GetBoolean(), id);
        Assert.Equal("project", Item(document, 0).GetProperty("result").GetProperty("owner").GetString());
        Assert.DoesNotContain("FakeProjectUser!1", document.GetRawText());

        var (read, readRaw) = await session.ReadAsync(new { operationId = "u", operation = "get_umac" });
        Assert.False(readRaw.IsError == true, read.GetRawText());
        var result = Item(read, 0).GetProperty("result");
        var umac = Section(result.GetProperty("project"), "umac");
        var collections = umac.GetProperty("collections").EnumerateArray().ToDictionary(c => c.GetProperty("name").GetString()!, c => c.GetProperty("items"));
        var role = collections["CustomRoles"][0];
        Assert.Equal("Operator", role.GetProperty("key").GetString());
        Assert.Equal("Open project read-only", Property(role, "AssignedEngineeringRights"));
        var user = collections["ProjectUsers"][0];
        Assert.Equal("Anna", Property(user, "AliasName"));
        Assert.Equal("False", Property(user, "IsActive"));
        Assert.Equal("Operator", Property(user, "Roles"));
        var device = Section(result.GetProperty("plcs")[0].GetProperty("sections"), "umacDevice");
        var rights = device.GetProperty("collections").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "CustomRolesRights").GetProperty("items")[0];
        Assert.Equal("Read access", Property(rights, "AssignedRights"));
        Assert.Equal("10", Property(Section(result.GetProperty("project"), "umacPasswordPolicy"), "MinimumLength"));
    }

    [Fact]
    public async Task Umac_MissingRole_FailsTargetNotFound_NothingChanged()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);
        var (document, _) = await session.WriteAsync(false,
            new { operationId = "eng", operation = "assign_engineering_right", roleName = "Nobody", rightName = "Open project read-only" });
        var item = Item(document, 0);
        Assert.Equal("target_not_found", item.GetProperty("failure").GetProperty("category").GetString());
        Assert.Contains("Nothing was changed.", item.GetProperty("failure").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Umac_UserKind_IsValidated()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);
        var (document, raw) = await session.WriteAsync(false,
            new { operationId = "a", operation = "assign_user_role", userName = "x", roleName = "y", userKind = "admin" });
        Assert.True(raw.IsError == true);
        Assert.Contains("userKind must be one of", document.GetProperty("error").GetProperty("message").GetString());
        Assert.DoesNotContain(session.Methods, PlcSettingsCatalog.IsWrite);
    }
}

public sealed partial class PlcSettingsMcpTests
{
    [Fact]
    public async Task PasswordPolicies_ArePlannedAgainstTheCurrentValues()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);
        var (document, raw) = await session.WriteAsync(true,
            new { operationId = "weaker", operation = "set_umac_password_policy", settings = new Dictionary<string, object> { ["MinimumLength"] = 6 } },
            new { operationId = "stronger", operation = "set_umac_password_policy", settings = new Dictionary<string, object> { ["MinimumLength"] = 12 } },
            new { operationId = "attempts", operation = "set_account_locking", plcName = "PLC_1", settings = new Dictionary<string, object> { ["NumberOfFailedLoginAttempts"] = 10 } },
            new { operationId = "legacy", operation = "set_legacy_plc_password_policy", settings = new Dictionary<string, object> { ["MinimumLength"] = 9 } });
        Assert.False(raw.IsError == true, document.GetRawText());
        Assert.Equal(new[] { "plc_lowers_cpu_protection", "plc_changes_password_policy", "plc_lowers_cpu_protection", "plc_changes_password_policy" }, GuardIds(document));
        Assert.Contains(PlcUmacMethods.GetUmac, session.Methods);
        Assert.Contains(PlcCpuSettingsMethods.GetCpuSettings, session.Methods);
        Assert.DoesNotContain(session.Methods, PlcSettingsCatalog.IsWrite);
    }
}
