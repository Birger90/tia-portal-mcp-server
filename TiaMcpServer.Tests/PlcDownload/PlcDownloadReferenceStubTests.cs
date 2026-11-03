using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using TiaMcpServer.Tests.Diagnostics;
using Xunit;

namespace TiaMcpServer.Tests.PlcDownload;

/// <summary>
/// Pins the compile-only download declarations against the V21 type-name baseline (type names and
/// owning assemblies extracted from installed V21 DLL metadata), and that no stub enum carries an
/// invented member value the worker could compile in.
/// </summary>
public sealed class PlcDownloadReferenceStubTests
{
    private const string Download = "Siemens.Engineering.Download.";
    private const string Configurations = Download + "Configurations.";
    private const string Connection = "Siemens.Engineering.Connection.";

    public static TheoryData<string, string> DeclaredTypes => new()
    {
        { "Siemens.Engineering.Base", Connection + "ConfigurationMode" },
        { "Siemens.Engineering.Base", Connection + "ConfigurationModeComposition" },
        { "Siemens.Engineering.Base", Connection + "ConfigurationPcInterface" },
        { "Siemens.Engineering.Base", Connection + "ConfigurationPcInterfaceComposition" },
        { "Siemens.Engineering.Base", Connection + "ConfigurationTargetInterface" },
        { "Siemens.Engineering.Base", Connection + "ConfigurationTargetInterfaceComposition" },
        { "Siemens.Engineering.Base", Connection + "ConnectionConfiguration" },
        { "Siemens.Engineering.Base", Connection + "IConfiguration" },
        { "Siemens.Engineering.Base", Download + "DownloadConfigurationDelegate" },
        { "Siemens.Engineering.Base", Download + "DownloadOptions" },
        { "Siemens.Engineering.Base", Download + "DownloadProvider" },
        { "Siemens.Engineering.Base", Download + "DownloadResult" },
        { "Siemens.Engineering.Base", Download + "DownloadResultMessage" },
        { "Siemens.Engineering.Base", Download + "DownloadResultMessageComposition" },
        { "Siemens.Engineering.Base", Download + "DownloadResultState" },
        { "Siemens.Engineering.Base", Configurations + "AlarmTextLibrariesDownload" },
        { "Siemens.Engineering.Base", Configurations + "AlarmTextLibrariesDownloadSelections" },
        { "Siemens.Engineering.Base", Configurations + "DownloadConfiguration" },
        { "Siemens.Engineering.Base", Configurations + "OverwriteSystemData" },
        { "Siemens.Engineering.Base", Configurations + "OverwriteSystemDataSelections" },
        { "Siemens.Engineering.Base", Configurations + "StartModules" },
        { "Siemens.Engineering.Base", Configurations + "StartModulesSelections" },
        { "Siemens.Engineering.Base", Configurations + "StopModules" },
        { "Siemens.Engineering.Base", Configurations + "StopModulesSelections" },
        { "Siemens.Engineering.Step7", Configurations + "ConsistentBlocksDownload" },
        { "Siemens.Engineering.Step7", Configurations + "ConsistentBlocksDownloadSelections" },
        { "Siemens.Engineering.Step7", Configurations + "TargetForSoftware" },
        { "Siemens.Engineering.Step7", Configurations + "TargetForSoftwareSelections" },
    };

    [Theory]
    [MemberData(nameof(DeclaredTypes))]
    public void DownloadTypes_LiveInTheirBaselineAssemblyOnly(string assembly, string type)
    {
        Assert.Contains(type, PublicTypes(assembly));
        var other = assembly == "Siemens.Engineering.Base" ? "Siemens.Engineering.Step7" : "Siemens.Engineering.Base";
        Assert.DoesNotContain(type, PublicTypes(other));
    }

    [Fact]
    public void DownloadStubEnums_DeclareNoMemberValues()
    {
        var enums = DeclaredTypes.Select(row => ((string)row[0], (string)row[1]))
            .Where(row => row.Item2.EndsWith("Selections", StringComparison.Ordinal)
                || row.Item2.EndsWith("DownloadOptions", StringComparison.Ordinal)
                || row.Item2.EndsWith("DownloadResultState", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(8, enums.Length);
        foreach (var (assembly, type) in enums)
            Assert.Equal(new[] { "value__" }, Fields(assembly, type));
    }

    private static string Path(string assembly) => System.IO.Path.Combine(ReferenceStubArtifactTests.RepositoryRoot, "ref", assembly + ".dll");

    private static HashSet<string> PublicTypes(string assembly)
    {
        using var stream = File.OpenRead(Path(assembly));
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        return reader.TypeDefinitions.Select(reader.GetTypeDefinition)
            .Where(type => (type.Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.Public)
            .Select(type => reader.GetString(type.Namespace) + "." + reader.GetString(type.Name))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string[] Fields(string assembly, string type)
    {
        using var stream = File.OpenRead(Path(assembly));
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var definition = reader.TypeDefinitions.Select(reader.GetTypeDefinition)
            .Single(candidate => reader.GetString(candidate.Namespace) + "." + reader.GetString(candidate.Name) == type);
        return definition.GetFields().Select(field => reader.GetString(reader.GetFieldDefinition(field).Name)).ToArray();
    }
}
