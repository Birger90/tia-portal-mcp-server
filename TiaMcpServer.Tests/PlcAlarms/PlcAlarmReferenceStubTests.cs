using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using TiaMcpServer.Tests.Diagnostics;
using Xunit;

namespace TiaMcpServer.Tests.PlcAlarms;

/// <summary>
/// Pins the compile-only alarm, text list and supervision declarations: every declared type is one of the V21
/// Step7 type names (api-baselines/v21/step7.json, extracted from installed V21 DLL metadata) and lives only in the
/// Step7 reference, the new stub enum carries no invented member, and members whose return or property types the
/// sources do not document (bound at runtime) are not declared.
/// </summary>
public sealed class PlcAlarmReferenceStubTests
{
    private const string Base = "Siemens.Engineering.Base";
    private const string Step7 = "Siemens.Engineering.Step7";
    private const string Alarm = "Siemens.Engineering.SW.Alarm.";
    private const string TextLists = "Siemens.Engineering.SW.Alarm.TextLists.";
    private const string Supervision = "Siemens.Engineering.SW.Supervision.";

    public static TheoryData<string> DeclaredTypes => new()
    {
        Alarm + "AlarmClassDataProvider",
        Alarm + "AlarmClassExportImportResult",
        Alarm + "PlcAlarmTextListProvider",
        Alarm + "PlcAlarmTextProvider",
        Alarm + "PlcAlarmTextXlsxExportOption",
        Alarm + "TextListXlsxResult",
        TextLists + "PlcAlarmSystemTextlist",
        TextLists + "PlcAlarmSystemTextlistComposition",
        TextLists + "PlcAlarmTextlist",
        TextLists + "PlcAlarmTextlistGroup",
        TextLists + "PlcAlarmUserTextlist",
        TextLists + "PlcAlarmUserTextlistComposition",
        Supervision + "SupervisionProvider",
        Supervision + "SupervisionXlsxResult",
    };

    [Theory]
    [MemberData(nameof(DeclaredTypes))]
    public void Types_LiveInTheStep7ReferenceOnly(string type)
    {
        Assert.Contains(type, PublicTypes(Step7));
        Assert.DoesNotContain(type, PublicTypes(Base));
    }

    [Fact]
    public void ExportOptionEnum_DeclaresNoMemberValues()
        => Assert.Equal(new[] { "value__" }, Fields(Step7, Alarm + "PlcAlarmTextXlsxExportOption"));

    [Fact]
    public void DocumentedMembers_AreDeclared()
    {
        Assert.Contains("get_PlcAlarmTextlistGroup", Methods(Step7, "Siemens.Engineering.SW.PlcSoftware"));
        Assert.Equal(new[] { "get_PlcAlarmSystemTextlists", "get_PlcAlarmUserTextlists" },
            Methods(Step7, TextLists + "PlcAlarmTextlistGroup").Where(m => m.StartsWith("get_PlcAlarm", StringComparison.Ordinal)).OrderBy(m => m));
        Assert.Contains("Delete", Methods(Step7, TextLists + "PlcAlarmUserTextlist"));
        Assert.Contains("get_Name", Methods(Step7, TextLists + "PlcAlarmUserTextlist"));
        Assert.Equal(2, Methods(Step7, Alarm + "PlcAlarmTextListProvider").Count(m => m == "ExportToXlsx"));
        Assert.Contains("ImportFromXlsx", Methods(Step7, Alarm + "PlcAlarmTextListProvider"));
        Assert.Contains("Export", Methods(Step7, Alarm + "AlarmClassDataProvider"));
        Assert.Contains("Import", Methods(Step7, Alarm + "AlarmClassDataProvider"));
        Assert.Equal(new[] { "ExportSupervisionsToXlsx", "ImportSupervisionSettingsFromXlsx", "ImportSupervisionsFromXlsx" },
            Methods(Step7, Supervision + "SupervisionProvider").Where(m => m.Contains("Xlsx", StringComparison.Ordinal)).OrderBy(m => m));
    }

    /// <summary>Members reached only at runtime (undocumented return or property types) stay out of the stubs.</summary>
    [Theory]
    [InlineData(Alarm + "PlcAlarmTextProvider", "ExportInstanceTextsToXlsx")]
    [InlineData(Alarm + "PlcAlarmTextProvider", "ImportInstanceTextsFromXlsx")]
    [InlineData(TextLists + "PlcAlarmUserTextlistComposition", "CreateFrom")]
    [InlineData(TextLists + "PlcAlarmTextlist", "get_ID")]
    [InlineData(TextLists + "PlcAlarmTextlist", "get_ListRange")]
    [InlineData(TextLists + "PlcAlarmTextlist", "get_Comment")]
    [InlineData(TextLists + "PlcAlarmTextlist", "get_Name")]
    [InlineData(TextLists + "PlcAlarmUserTextlist", "get_ListRange")]
    [InlineData(TextLists + "PlcAlarmUserTextlist", "set_Name")]
    [InlineData(Alarm + "TextListXlsxResult", "get_State")]
    [InlineData(Alarm + "TextListXlsxResult", "get_LogFilePath")]
    [InlineData(Alarm + "AlarmClassExportImportResult", "get_State")]
    [InlineData(Alarm + "AlarmClassExportImportResult", "get_Messages")]
    [InlineData(Supervision + "SupervisionXlsxResult", "get_State")]
    public void RuntimeOnlyMembers_AreNotDeclared(string type, string member)
        => Assert.DoesNotContain(member, Methods(Step7, type));

    [Fact]
    public void UndocumentedOrUnusedTypes_AreNotDeclared()
    {
        var types = PublicTypes(Step7);
        Assert.DoesNotContain(Alarm + "PlcAlarmTextXlsxResult", types);
        Assert.DoesNotContain(Alarm + "TextListXlsxResultState", types);
        Assert.DoesNotContain(TextLists + "PlcAlarmTextlistListRange", types);
        Assert.DoesNotContain(Supervision + "SupervisionSettingsProvider", types);
        Assert.DoesNotContain("Siemens.Engineering.SW.Alarm.PlcAlarm", types);
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
