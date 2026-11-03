using TiaMcpServer.Tools;
using TiaMcpServer.Vci;
using Xunit;

namespace TiaMcpServer.Tests.Vci;

public sealed class VciGuardDefinitionsTests
{
    [Fact]
    public void FiveGuardsHaveFixedSeveritiesAndProductionCatalogHasNoDuplicates()
    {
        var catalog = WriteGuardRegistration.ProductionCatalog();
        Assert.Equal(5, VciGuardDefinitions.Definitions.Count);
        Assert.DoesNotContain(VciGuardDefinitions.Definitions, g => g.Id == "vci_file_key_collision");
        // Owner decision 2026-11-01 (never stricter than TIA Portal): the one block is a name Windows cannot represent on disk.
        Assert.Equal("block", catalog.Get("vci_name_not_representable").Severity);
        foreach (var id in new[] { "vci_files_remain", "vci_import_unvalidated", "vci_workspace_exists", "vci_state_unverifiable" })
            Assert.Equal("info", catalog.Get(id).Severity);
        Assert.Equal(5, VciGuardDefinitions.Definitions.Select(g => g.Id).Distinct().Count());
    }
}
