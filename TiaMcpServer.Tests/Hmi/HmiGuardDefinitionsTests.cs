using TiaMcpServer.Hmi;
using TiaMcpServer.Tools;
using Xunit;
namespace TiaMcpServer.Tests.Hmi;
public class HmiGuardDefinitionsTests
{
    [Fact] public void ProductionCatalog_RegistersExactlyElevenHmiGuards()
    {
        var catalog = WriteGuardRegistration.ProductionCatalog();
        Assert.Equal(11, HmiGuardDefinitions.Definitions.Count);
        Assert.Equal("info", catalog.Get(HmiGuardDefinitions.DeletesConfiguration).Severity);
        Assert.Equal("info", catalog.Get(HmiGuardDefinitions.AuditPropertyUnverified).Severity);
        Assert.Equal("info", catalog.Get(HmiGuardDefinitions.DeletesScripting).Severity);
        Assert.Equal("info", catalog.Get(HmiGuardDefinitions.ClassicDeletes).Severity);
        Assert.Equal("info", catalog.Get(HmiGuardDefinitions.ClassicImportOverrides).Severity);
        Assert.Equal("info", catalog.Get(HmiGuardDefinitions.TextListFileUnverified).Severity);
        Assert.Equal("info", catalog.Get(HmiGuardDefinitions.DeletesScreen).Severity);
        Assert.Equal("info", catalog.Get(HmiGuardDefinitions.RenameUpdatesNoReferences).Severity);
        Assert.Equal("info", catalog.Get("hmi_state_unverifiable").Severity);
        Assert.Equal("info", catalog.Get("hmi_deletes_table_contents").Severity);
        Assert.Equal("info", catalog.Get("hmi_deletes_referenced_tag").Severity);
        Assert.DoesNotContain(HmiGuardDefinitions.Definitions, d => d.Id == "hmi_name_collision");
    }
}
