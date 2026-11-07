using TiaMcpServer.Contracts.Plc.Settings;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Plc;
using Xunit;

namespace TiaMcpServer.Tests.Plc.Settings;

/// <summary>Package G: protect_project and the UMC additions through plc_read/plc_write against the FakeWorker scenario plc-settings (stand-ins only).</summary>
public sealed partial class PlcSettingsMcpTests
{
    private const string UmcServerVariable = "TIA_MCP_TEST_UMC_PASSWORD";

    [Fact]
    public void UmacMore_Catalog_Policy_AndGuards()
    {
        Environment.SetEnvironmentVariable(UmcServerVariable, "UmcServer!1");
        foreach (var method in PlcUmacMethods.UmcServerReads)
        {
            Assert.True(PlcSettingsCatalog.IsRead(method));
            Assert.Equal(OperationCapability.OnlineObserve, OperationPolicyCatalog.GetCapability(method));
        }

        foreach (var method in new[] { PlcUmacMethods.ProtectProject, PlcUmacMethods.CreateOfflineUmcUser, PlcUmacMethods.CreateOfflineUmcUserGroup, PlcUmacMethods.ImportUmcUser })
            Assert.Equal(OperationCapability.ProjectMutation, OperationPolicyCatalog.GetCapability(method));
        Assert.Contains(PlcGuardDefinitions.Definitions, d => d.Id == PlcGuardDefinitions.ProtectsProject && d.Severity == TiaMcpServer.Safety.Pipeline.WriteGuardSeverities.Acknowledge);
        var halfCredentials = PlcOperationCatalog.ValidateWrite(new[] { new PlcOperationRequest { OperationId = "i", Operation = PlcUmacMethods.ImportUmcUser, Name = "x", UserName = "umcadmin" } });
        Assert.False(halfCredentials.IsValid);
        Assert.Contains("together", halfCredentials.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UmacMore_ProtectProject_FiresTheAcknowledgeGuard_AndIsReadBack()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (preview, _) = await session.WriteAsync(true, new { operationId = "p", operation = "protect_project", userName = "admin", passwordVariable = ProjectUserVariable });
        Assert.Contains("plc_protects_project", GuardIds(preview));
        var (document, raw) = await session.WriteAsync(false, new { operationId = "p", operation = "protect_project", userName = "admin", passwordVariable = ProjectUserVariable });
        Assert.False(raw.IsError == true, document.GetRawText());
        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.True(Verification(document, "p").GetProperty("success").GetBoolean());
        Assert.DoesNotContain("FakeProjectUser!1", document.GetRawText());
    }

    [Fact]
    public async Task UmacMore_OfflineUmcObjects_AndTheUmcServer_WithCredentialsFromASource()
    {
        Environment.SetEnvironmentVariable(UmcServerVariable, "UmcServer!1");
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);
        var (document, raw) = await session.WriteAsync(false,
            new { operationId = "u", operation = "create_offline_umc_user", userName = "DOMAIN\\offline" },
            new { operationId = "g", operation = "create_offline_umc_user_group", name = "DOMAIN\\Planners" },
            new { operationId = "i", operation = "import_umc_user", name = "DOMAIN\\operator", userName = "umcadmin", passwordVariable = UmcServerVariable },
            new { operationId = "ig", operation = "import_umc_user", name = "DOMAIN\\Operators", userKind = "umcUserGroup", userName = "umcadmin", passwordVariable = UmcServerVariable });
        Assert.False(raw.IsError == true, document.GetRawText());
        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        foreach (var id in new[] { "u", "g", "i", "ig" })
            Assert.True(Verification(document, id).GetProperty("success").GetBoolean(), id);
        Assert.DoesNotContain("UmcServer!1", document.GetRawText());

        var (missing, _) = await session.WriteAsync(false, new { operationId = "m", operation = "import_umc_user", name = "DOMAIN\\nobody", userName = "umcadmin", passwordVariable = UmcServerVariable });
        Assert.Equal("target_not_found", Item(missing, 0).GetProperty("failure").GetProperty("category").GetString());

        var (check, checkRaw) = await session.ReadAsync(new { operationId = "c", operation = "check_umc_consistency", userName = "umcadmin", passwordVariable = UmcServerVariable });
        Assert.False(checkRaw.IsError == true, check.GetRawText());
        Assert.Equal("True", Property(Section(Item(check, 0).GetProperty("result").GetProperty("project"), "umcConsistency"), "Consistent"));
        var (user, _) = await session.ReadAsync(new { operationId = "s", operation = "get_umc_server_user", name = "DOMAIN\\Operators", userKind = "umcUserGroup", userName = "umcadmin", passwordVariable = UmcServerVariable });
        var section = Section(Item(user, 0).GetProperty("result").GetProperty("project"), "umcServerUser");
        Assert.Equal("Operators of line 1", Property(section, "Description"));
    }
}
