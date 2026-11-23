using System.Security;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.PlcDownload;
using TiaMcpServer.ProjectLifecycle;
using TiaMcpServer.Safety.Pipeline;
using Xunit;

namespace TiaMcpServer.Tests.Project;

/// <summary>The UMAC login of open_project (Sparkz400/TIA_MCP 315493b idea, Apache-2.0): binding, one answer, host shape rules.</summary>
public sealed class UmacProjectLoginTests
{
    [Fact]
    public void Binding_FindsTheDocumentedMembers_AndAnswersTypeNameAndPassword()
    {
        var binding = UmacProjectLoginBinding.Bind(typeof(Siemens.Engineering.UmacFakeComposition), UmacProjectLoginNames.GlobalUser);
        Assert.Equal(typeof(Siemens.Engineering.UmacDelegate), binding.DelegateType);
        using var password = Secret("geheim");
        var run = new UmacProjectLoginRun(binding, "Engineer", password);
        var composition = new Siemens.Engineering.UmacFakeComposition();

        var opened = RuntimeServiceValues.Call(binding.Open, composition, new object?[] { new FileInfo("/tmp/p.ap21"), run.Handler });

        Assert.Equal("opened", opened);
        Assert.Equal(1, run.Requests);
        var credentials = Assert.Single(composition.Asked);
        Assert.Equal(Siemens.Engineering.UmacUserType.Global, credentials.Type);
        Assert.Equal("Engineer", credentials.Name);
        Assert.Equal(6, credentials.PasswordLength);
        Assert.DoesNotContain("geheim", run.Describe(UmacProjectLoginNames.GlobalUser, "environment"));
    }

    [Fact]
    public void ASecondLoginRequest_IsRefused_AndThePasswordIsNotResent()
    {
        var binding = UmacProjectLoginBinding.Bind(typeof(Siemens.Engineering.UmacFakeComposition), UmacProjectLoginNames.ProjectUser);
        using var password = Secret("falsch");
        var run = new UmacProjectLoginRun(binding, "Engineer", password);
        var composition = new Siemens.Engineering.UmacFakeComposition { AskTwice = true };

        var refusal = Assert.Throws<RuntimeBindingException>(() =>
            RuntimeServiceValues.Call(binding.Open, composition, new object?[] { new FileInfo("/tmp/p.ap21"), run.Handler }));

        Assert.Equal(WorkerFailureCategories.AccessDenied, refusal.Category);
        Assert.Equal(2, run.Requests);
        Assert.Single(composition.Asked, c => c.PasswordLength > 0);
    }

    [Fact]
    public void AMissingOverload_IsApiMemberMissing()
    {
        var missing = Assert.Throws<RuntimeBindingException>(() => UmacProjectLoginBinding.Bind(typeof(string), UmacProjectLoginNames.ProjectUser));
        Assert.Equal(WorkerFailureCategories.ApiMemberMissing, missing.Category);
        Assert.Contains("Nothing was opened", missing.Message);
    }

    [Theory]
    [InlineData(null, "project", "PW_VAR", "need umacUserName")]
    [InlineData("Engineer", "admin", "PW_VAR", "umacUserType")]
    [InlineData("Eng\u0001ineer", null, "PW_VAR", "control characters")]
    public void ShapeErrors_AreReportedWithoutTheValues(string? user, string? type, string password, string expected)
    {
        var item = new LifecycleWriteItem("open_project") { ProjectPath = "C:/p/Project.ap21", UmacUserName = user, UmacUserType = type, UmacPassword = password };
        var error = UmacOpenItem.Error(item);
        Assert.NotNull(error);
        Assert.Contains(expected, error);
    }

    [Fact]
    public void ALocalSession_OrUpgrade_IsNotOfferedWithALogin()
    {
        var local = new LifecycleWriteItem("open_project") { ProjectPath = "C:/p/Session.als21", UmacUserName = "Engineer", UmacPassword = "PW_VAR" };
        Assert.Contains(".als21", UmacOpenItem.Error(local));
        var upgrade = new LifecycleWriteItem("open_project") { ProjectPath = "C:/p/Project.ap21", UmacUserName = "Engineer", UmacPassword = "PW_VAR", Upgrade = true };
        Assert.Contains("upgrade", UmacOpenItem.Error(upgrade));
    }

    [Fact]
    public void APasswordValue_IsRedactedWhileBinding_AndRefused()
    {
        var item = UmacOpenItem.Build(new LifecycleWriteItem("open_project") { ProjectPath = "C:/p/Project.ap21" },
            "Engineer", null, "my secret password!", _ => null);
        Assert.Equal(PlcDownloadPasswordMap.RedactedVariable, item.UmacPassword);
        var error = UmacOpenItem.Error(item);
        Assert.NotNull(error);
        Assert.DoesNotContain("my secret", error);
    }

    [Fact]
    public void AnUnsetVariable_IsRefused_AndASetOneIsKept()
    {
        var unset = UmacOpenItem.Build(new LifecycleWriteItem("open_project") { ProjectPath = "C:/p/Project.ap21" }, "Engineer", null, "UMAC_PW", _ => null);
        Assert.Contains("not set", UmacOpenItem.Error(unset));
        var set = UmacOpenItem.Build(new LifecycleWriteItem("open_project") { ProjectPath = "C:/p/Project.ap21" }, "Engineer", "global", "UMAC_PW", _ => "x");
        Assert.Null(UmacOpenItem.Error(set));
        Assert.Equal("UMAC_PW", set.UmacPassword);
        var sentence = UmacOpenItem.PromptSentence(set);
        Assert.Contains(ConfirmationText.Quote("Engineer"), sentence);
        Assert.Contains("global user", sentence);
        Assert.Contains("environment source", sentence);
        Assert.DoesNotContain("UMAC_PW", sentence);
    }

    [Fact]
    public void NoUmacField_LeavesTheItemUnchanged()
    {
        var item = new LifecycleWriteItem("open_project") { ProjectPath = "C:/p/Project.ap21" };
        Assert.Same(item, UmacOpenItem.Build(item, null, null, null, _ => null));
        Assert.Null(UmacOpenItem.Error(item));
    }

    [Fact]
    public void ThePurpose_IsKnownToThePasswordManager_AndNeverADownloadAnswer()
    {
        Assert.NotNull(TiaMcpServer.Contracts.Shared.PasswordManagement.PasswordPurposes.Find(UmacProjectLoginNames.PasswordPurpose));
        Assert.True(PasswordMapFile.IsPurpose(UmacProjectLoginNames.PasswordPurpose));
    }

    private static SecureString Secret(string text)
    {
        var secure = new SecureString();
        foreach (var c in text) secure.AppendChar(c);
        secure.MakeReadOnly();
        return secure;
    }
}
