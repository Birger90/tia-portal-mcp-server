using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using TiaMcpServer.Tests.Diagnostics;
using Xunit;

namespace TiaMcpServer.Tests.Library;

/// <summary>
/// Pins the compile-only library declarations against the V21 type-name baseline (type names and owning
/// assemblies extracted from installed V21 DLL metadata), that no new stub enum carries an invented member
/// value, and that members bound only at runtime (undocumented return, declaring or collection types) are not declared.
/// </summary>
public sealed class LibraryReferenceStubTests
{
    private const string Base = "Siemens.Engineering.Base";
    private const string Step7 = "Siemens.Engineering.Step7";
    private const string Lib = "Siemens.Engineering.Library.";

    public static TheoryData<string, string> DeclaredTypes => new()
    {
        { Base, "Siemens.Engineering.OpenMode" },
        { Base, Lib + "GlobalLibrary" },
        { Base, Lib + "GlobalLibraryComposition" },
        { Base, Lib + "ILibrary" },
        { Base, Lib + "UserGlobalLibrary" },
        { Base, Lib + "SystemGlobalLibrary" },
        { Base, Lib + "CorporateGlobalLibrary" },
        { Base, Lib + "MasterCopies.IMasterCopySource" },
        { Base, Lib + "MasterCopies.MasterCopyMode" },
        { Base, Lib + "Types.CreateOrReleaseDependenciesMode" },
        { Base, Lib + "Types.IInstanceSearchScope" },
        { Base, Lib + "Types.ILibraryTypeOrFolderSelection" },
        { Base, Lib + "Types.IUpdateProjectScope" },
        { Base, Lib + "Types.LibraryType" },
        { Base, Lib + "Types.LibraryTypeFolder" },
        { Base, Lib + "Types.LibraryTypeInstanceInfo" },
        { Base, Lib + "Types.LibraryTypeSystemFolder" },
        { Base, Lib + "Types.LibraryTypeUserFolder" },
        { Base, Lib + "Types.LibraryTypeVersion" },
        { Base, Lib + "Types.LibraryTypeVersionState" },
        { Base, Lib + "Types.UpdateCheckMode" },
        { Base, Lib + "Types.UpdatePathsMode" },
        { Step7, "Siemens.Engineering.SW.Blocks.CodeBlockLibraryType" },
        { Step7, "Siemens.Engineering.SW.Blocks.CodeBlockLibraryTypeVersion" },
        { Step7, "Siemens.Engineering.SW.Types.PlcTypeLibraryType" },
        { Step7, "Siemens.Engineering.SW.Types.PlcTypeLibraryTypeVersion" },
    };

    [Theory]
    [MemberData(nameof(DeclaredTypes))]
    public void Types_LiveInTheirBaselineAssemblyOnly(string assembly, string type)
    {
        Assert.Contains(type, PublicTypes(assembly));
        Assert.DoesNotContain(type, PublicTypes(assembly == Base ? Step7 : Base));
    }

    [Theory]
    [InlineData("Siemens.Engineering.OpenMode")]
    [InlineData(Lib + "MasterCopies.MasterCopyMode")]
    [InlineData(Lib + "Types.CreateOrReleaseDependenciesMode")]
    [InlineData(Lib + "Types.LibraryTypeVersionState")]
    [InlineData(Lib + "Types.UpdateCheckMode")]
    [InlineData(Lib + "Types.UpdatePathsMode")]
    public void NewStubEnums_DeclareNoMemberValues(string type)
        => Assert.Equal(new[] { "value__" }, Fields(Base, type));

    /// <summary>Members reached only at runtime stay out of the stubs.</summary>
    [Theory]
    [InlineData(Lib + "GlobalLibraryComposition", "GetGlobalLibraryInfos")]
    [InlineData(Lib + "GlobalLibraryComposition", "Open")]
    [InlineData(Lib + "GlobalLibraryComposition", "OpenWithUpgrade")]
    [InlineData(Lib + "GlobalLibraryComposition", "Retrieve")]
    [InlineData(Lib + "GlobalLibraryComposition", "Create")]
    [InlineData(Lib + "UserGlobalLibrary", "Save")]
    [InlineData(Lib + "GlobalLibrary", "Close")]
    [InlineData(Lib + "GlobalLibrary", "UpdateCheck")]
    [InlineData(Lib + "GlobalLibrary", "UpdateProject")]
    [InlineData(Lib + "GlobalLibrary", "CompareToLibrary")]
    [InlineData(Lib + "ProjectLibrary", "CompareToLibrary")]
    [InlineData(Lib + "ProjectLibrary", "UpdateProject")]
    [InlineData(Lib + "MasterCopies.MasterCopy", "Delete")]
    [InlineData(Lib + "MasterCopies.MasterCopy", "get_ContentDescriptions")]
    [InlineData(Lib + "Types.LibraryType", "get_Versions")]
    [InlineData(Lib + "Types.LibraryTypeFolder", "get_Folders")]
    [InlineData(Lib + "Types.LibraryTypeFolder", "get_Types")]
    [InlineData(Lib + "Types.LibraryTypeVersion", "Release")]
    [InlineData(Lib + "Types.LibraryTypeVersion", "Discard")]
    [InlineData(Lib + "Types.LibraryTypeVersion", "SetAsDefault")]
    [InlineData(Lib + "Types.LibraryTypeVersion", "Delete")]
    [InlineData(Lib + "Types.LibraryTypeVersion", "FindInstances")]
    [InlineData(Lib + "Types.LibraryTypeVersion", "get_VersionNumber")]
    [InlineData(Lib + "Types.LibraryTypeInstanceInfo", "get_LibraryTypeVersion")]
    public void RuntimeOnlyMembers_AreNotDeclared(string type, string member)
        => Assert.DoesNotContain(member, Methods(Base, type));

    [Fact]
    public void TargetCompositionCreateFrom_IsNotDeclared()
    {
        Assert.DoesNotContain("CreateFrom", Methods(Step7, "Siemens.Engineering.SW.Blocks.PlcBlockComposition"));
        Assert.DoesNotContain("CreateFrom", Methods(Step7, "Siemens.Engineering.SW.Types.PlcTypeComposition"));
        Assert.DoesNotContain("CreateFrom", Methods(Base, "Siemens.Engineering.HW.DeviceComposition"));
        Assert.Contains("CreateFrom", Methods(Base, Lib + "MasterCopies.MasterCopyComposition"));
    }

    [Fact]
    public void UndocumentedResultTypes_AreNotDeclared()
    {
        var types = PublicTypes(Base);
        Assert.DoesNotContain(Lib + "GlobalLibraryInfo", types);
        Assert.DoesNotContain(Lib + "Types.UpdateCheckResult", types);
        Assert.DoesNotContain(Lib + "Compare.LibraryCompareResult", types);
        Assert.DoesNotContain(Lib + "Types.LibraryTypeVersionComposition", types);
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
