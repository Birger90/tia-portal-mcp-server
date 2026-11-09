using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using TiaMcpServer.Tests.Diagnostics;
using Xunit;

namespace TiaMcpServer.Tests.PlcOnline;

/// <summary>
/// Pins the compile-only compare, online, station-upload, library and watch/force table declarations
/// against the V21 type-name baseline (type names and owning assemblies extracted from installed V21
/// DLL metadata), and that no new stub enum carries an invented member value the worker could compile in.
/// </summary>
public sealed class PlcOnlineReferenceStubTests
{
    private const string Base = "Siemens.Engineering.Base";
    private const string Step7 = "Siemens.Engineering.Step7";
    private const string Tables = "Siemens.Engineering.SW.WatchAndForceTables.";

    public static TheoryData<string, string> DeclaredTypes => new()
    {
        { Base, "Siemens.Engineering.Compare.CompareResult" },
        { Base, "Siemens.Engineering.Compare.CompareResultElement" },
        { Base, "Siemens.Engineering.Compare.CompareResultElementComposition" },
        { Base, "Siemens.Engineering.Compare.CompareResultState" },
        { Base, "Siemens.Engineering.Compare.ISoftwareCompareTarget" },
        { Base, "Siemens.Engineering.Online.OnlineProvider" },
        { Base, "Siemens.Engineering.Online.OnlineState" },
        { Base, "Siemens.Engineering.Online.OnlineConfigurationDelegate" },
        { Base, "Siemens.Engineering.Online.Configurations.OnlineConfiguration" },
        { Base, "Siemens.Engineering.Connection.ConfigurationAddress" },
        { Base, "Siemens.Engineering.Connection.ConfigurationAddressComposition" },
        { Base, "Siemens.Engineering.Upload.StationUploadProvider" },
        { Base, "Siemens.Engineering.Upload.UploadConfigurationDelegate" },
        { Base, "Siemens.Engineering.Upload.UploadResult" },
        { Base, "Siemens.Engineering.Upload.UploadResultMessage" },
        { Base, "Siemens.Engineering.Upload.UploadResultMessageComposition" },
        { Base, "Siemens.Engineering.Upload.UploadResultState" },
        { Base, "Siemens.Engineering.Upload.Configurations.UploadConfiguration" },
        { Base, "Siemens.Engineering.Library.ProjectLibrary" },
        { Base, "Siemens.Engineering.Library.MasterCopies.MasterCopy" },
        { Base, "Siemens.Engineering.Library.MasterCopies.MasterCopyComposition" },
        { Base, "Siemens.Engineering.Library.MasterCopies.MasterCopyFolder" },
        { Base, "Siemens.Engineering.Library.MasterCopies.MasterCopySystemFolder" },
        { Step7, Tables + "PlcForceTable" },
        { Step7, Tables + "PlcForceTableComposition" },
        { Step7, Tables + "PlcWatchAndForceTableGroup" },
        { Step7, Tables + "PlcWatchAndForceTableSystemGroup" },
        { Step7, Tables + "PlcWatchAndForceTableUserGroup" },
        { Step7, Tables + "PlcWatchAndForceTableUserGroupComposition" },
        { Step7, Tables + "PlcWatchTable" },
        { Step7, Tables + "PlcWatchTableComposition" },
    };

    [Theory]
    [MemberData(nameof(DeclaredTypes))]
    public void Types_LiveInTheirBaselineAssemblyOnly(string assembly, string type)
    {
        Assert.Contains(type, PublicTypes(assembly));
        Assert.DoesNotContain(type, PublicTypes(assembly == Base ? Step7 : Base));
    }

    [Theory]
    [InlineData("Siemens.Engineering.Compare.CompareResultState")]
    [InlineData("Siemens.Engineering.Online.OnlineState")]
    [InlineData("Siemens.Engineering.Upload.UploadResultState")]
    public void NewStubEnums_DeclareNoMemberValues(string type)
        => Assert.Equal(new[] { "value__" }, Fields(Base, type));

    /// <summary>Members reached only at runtime stay out of the stubs: their signatures are undocumented.</summary>
    [Theory]
    [InlineData(Step7, "Siemens.Engineering.SW.PlcSoftware", "UpdateProgram")]
    [InlineData(Step7, Tables + "PlcWatchTable", "get_Entries")]
    [InlineData(Step7, Tables + "PlcForceTable", "get_Entries")]
    public void RuntimeOnlyMembers_AreNotDeclared(string assembly, string type, string member)
        => Assert.DoesNotContain(member, Methods(assembly, type));

    [Fact]
    public void EntryAndCallbackTypes_AreNotDeclared()
    {
        var step7 = PublicTypes(Step7);
        Assert.DoesNotContain(Tables + "PlcWatchTableEntry", step7);
        Assert.DoesNotContain(Tables + "PlcForceTableEntry", step7);
        Assert.DoesNotContain("Siemens.Engineering.Upload.Configurations.UploadMissingProducts", PublicTypes(Base));
    }

    private static string Path(string assembly) => System.IO.Path.Combine(TiaMcpServer.Tests.TestSupport.RepositoryFiles.Root, "ref", assembly + ".dll");

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

    private static TypeDefinition Definition(MetadataReader reader, string type)
        => reader.TypeDefinitions.Select(reader.GetTypeDefinition)
            .Single(candidate => reader.GetString(candidate.Namespace) + "." + reader.GetString(candidate.Name) == type);

    private static string[] Fields(string assembly, string type)
    {
        using var stream = File.OpenRead(Path(assembly));
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        return Definition(reader, type).GetFields().Select(field => reader.GetString(reader.GetFieldDefinition(field).Name)).ToArray();
    }

    private static string[] Methods(string assembly, string type)
    {
        using var stream = File.OpenRead(Path(assembly));
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        return Definition(reader, type).GetMethods().Select(method => reader.GetString(reader.GetMethodDefinition(method).Name)).ToArray();
    }
}
