using TiaMcpServer.Contracts.Plc.Interface;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

/// <summary>
/// The SimaticML interface model against the repository's real V21 exports (the AnalogInputSettings
/// PLC data type and the LAD OB bundle) and the generator's compact documents. Every edit is pinned
/// as an exact text change of the original document: whatever the edit does not touch stays
/// byte-identical.
/// </summary>
public sealed class SimaticMlInterfaceDocumentTests
{
    private static string Udt() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "AnalogInputSettings.xml"));

    private static string ObXml()
    {
        var bundle = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "get_block_content.ob-lad.v2.bundle.txt"));
        Assert.True(PlcBlockBundleText.TryGetSimaticMl(bundle, out var name, out var xml));
        Assert.Equal("Main.xml", name);
        return xml;
    }

    private static string Apply(string xml, PlcInterfaceEditRequest edit, out PlcInterfaceEditOutcome outcome)
    {
        var document = SimaticMlInterfaceDocument.Parse(xml);
        outcome = document.Apply(edit);
        return document.Serialize();
    }

    private static PlcInterfaceEditRequest Update(string section, string path) => new()
    {
        Kind = PlcInterfaceEditKind.Update, Section = section, Path = PlcMemberPath.Parse(path, allowIndices: false),
    };

    [Fact]
    public void UnchangedDocumentsSerializeByteIdentically()
    {
        Assert.Equal(Udt(), SimaticMlInterfaceDocument.Parse(Udt()).Serialize());
        Assert.Equal(ObXml(), SimaticMlInterfaceDocument.Parse(ObXml()).Serialize());
        var crlf = Udt().Replace("\n", "\r\n");
        Assert.Equal(crlf, SimaticMlInterfaceDocument.Parse(crlf).Serialize());
    }

    [Fact]
    public void ReadsTheMemberTreeOfAPlcDataType()
    {
        var document = SimaticMlInterfaceDocument.Parse(Udt());

        Assert.Equal("SW.Types.PlcStruct", document.DocumentElement);
        Assert.Equal("AnalogInputSettings", document.ObjectName);
        Assert.False(document.IsSafety);
        var section = Assert.Single(document.ReadSections());
        Assert.Equal("None", section.Name);
        Assert.Equal(new[] { "Scaling", "Limits" }, section.Members.Select(m => m.Name));
        var type = section.Members[0].Members[0];
        Assert.Equal(("Scaling.Type", "Bool", "TRUE"), (type.Path, type.DataType, type.StartValue));
        Assert.Equal("0- multiply, 1 - scale", Assert.Single(type.Comments).Text);
        Assert.Equal("en-US", type.Comments[0].Culture);
        Assert.Empty(type.Attributes);
        Assert.Empty(type.OtherElements);
        Assert.Equal("T#1s", section.Members[1].Members.Single(m => m.Name == "regAlarmDelay").StartValue);
        Assert.Null(section.Members[0].Members.Single(m => m.Name == "valueMin").StartValue);
    }

    [Fact]
    public void ReadsInformativeOrganizationBlockMembers()
    {
        var sections = SimaticMlInterfaceDocument.Parse(ObXml()).ReadSections();

        Assert.Equal(new[] { "Input", "Temp", "Constant" }, sections.Select(s => s.Name));
        Assert.All(sections[0].Members, member => Assert.True(member.Informative));
        Assert.Equal(new[] { "Initial_Call", "Remanence" }, sections[0].Members.Select(m => m.Name));
    }

    [Fact]
    public void UpdatingAStartValueChangesOnlyThatElement()
    {
        var edit = Update("None", "Scaling.regMax");
        edit.StartValue = "32000";

        var edited = Apply(Udt(), edit, out var outcome);

        Assert.Equal(Udt().Replace("<StartValue>27648</StartValue>", "<StartValue>32000</StartValue>"), edited);
        var change = Assert.Single(outcome.Changes);
        Assert.Equal(("startValue", "27648", "32000"), (change.Field, change.Current, change.Requested));
        Assert.True(outcome.Expectation.Check(SimaticMlInterfaceDocument.Parse(edited).ReadSections()).Success);
        Assert.False(outcome.Expectation.Check(SimaticMlInterfaceDocument.Parse(Udt()).ReadSections()).Success);
    }

    [Fact]
    public void AddingAMemberIndentsItLikeItsSiblings()
    {
        var edit = new PlcInterfaceEditRequest
        {
            Kind = PlcInterfaceEditKind.Add, Section = "None", Path = PlcMemberPath.Parse("Gain", false), DataType = "Real", StartValue = "1.5",
        };

        var edited = Apply(Udt(), edit, out var outcome);

        Assert.Equal(Udt().Replace(
            "            </Member>\n          </Section>",
            "            </Member>\n            <Member Name=\"Gain\" Datatype=\"Real\">\n              <StartValue>1.5</StartValue>\n            </Member>\n          </Section>"), edited);
        Assert.Equal(new[] { "member", "dataType", "startValue" }, outcome.Changes.Select(c => c.Field));
        Assert.True(outcome.Expectation.Check(SimaticMlInterfaceDocument.Parse(edited).ReadSections()).Success);
    }

    [Fact]
    public void AddingANestedMemberBeforeASiblingWithAComment()
    {
        var edit = new PlcInterfaceEditRequest
        {
            Kind = PlcInterfaceEditKind.Add, Section = "None", Path = PlcMemberPath.Parse("Limits.regDeadband", false), DataType = "Int",
            Comment = "Band", CommentCulture = "en-US", InsertBefore = "regAlarmDelay",
        };

        var edited = Apply(Udt(), edit, out _);

        Assert.Equal(Udt().Replace(
            "              <Member Name=\"regAlarmDelay\"",
            "              <Member Name=\"regDeadband\" Datatype=\"Int\">\n                <Comment>\n                  <MultiLanguageText Lang=\"en-US\">Band</MultiLanguageText>\n                </Comment>\n              </Member>\n              <Member Name=\"regAlarmDelay\""), edited);
    }

    [Fact]
    public void AddingToAnEmptySectionOpensIt()
    {
        var edit = new PlcInterfaceEditRequest { Kind = PlcInterfaceEditKind.Add, Section = "Temp", Path = PlcMemberPath.Parse("tmp", false), DataType = "Int" };

        var edited = Apply(ObXml(), edit, out _);

        Assert.Equal(ObXml().Replace("<Section Name=\"Temp\" />",
            "<Section Name=\"Temp\">\n            <Member Name=\"tmp\" Datatype=\"Int\" />\n          </Section>"), edited);
    }

    [Fact]
    public void DeletingRemovesTheMemberAndItsLine()
    {
        var edit = new PlcInterfaceEditRequest { Kind = PlcInterfaceEditKind.Delete, Section = "None", Path = PlcMemberPath.Parse("Limits.regAlarmDelay", false) };

        var edited = Apply(Udt(), edit, out var outcome);

        Assert.Equal(Udt().Replace("\n              <Member Name=\"regAlarmDelay\" Datatype=\"Time\">\n                <StartValue>T#1s</StartValue>\n              </Member>", string.Empty), edited);
        Assert.Equal(("member", "None.Limits.regAlarmDelay", (string?)null), (outcome.Changes[0].Field, outcome.Changes[0].Current, outcome.Changes[0].Requested));
        Assert.True(outcome.Expectation.Check(SimaticMlInterfaceDocument.Parse(edited).ReadSections()).Success);
    }

    [Fact]
    public void DeletingTheLastMemberClosesTheSection()
    {
        var add = new PlcInterfaceEditRequest { Kind = PlcInterfaceEditKind.Add, Section = "Temp", Path = PlcMemberPath.Parse("tmp", false), DataType = "Int" };
        var added = Apply(ObXml(), add, out _);

        var removed = Apply(added, new PlcInterfaceEditRequest { Kind = PlcInterfaceEditKind.Delete, Section = "Temp", Path = PlcMemberPath.Parse("tmp", false) }, out _);

        Assert.Equal(ObXml(), removed);
    }

    [Fact]
    public void RenameAndDataTypeChangeKeepEverythingElse()
    {
        var edit = Update("None", "Limits.regMin");
        edit.NewName = "lowLimit";
        edit.DataType = "DInt";
        // A data type change states the start value for the new type explicitly (here the same literal).
        edit.StartValue = "-500";

        var edited = Apply(Udt(), edit, out var outcome);

        Assert.Equal(Udt().Replace("<Member Name=\"regMin\" Datatype=\"Int\">\n                <StartValue>-500</StartValue>",
            "<Member Name=\"lowLimit\" Datatype=\"DInt\">\n                <StartValue>-500</StartValue>"), edited);
        Assert.Equal("Limits.lowLimit", outcome.Expectation.Path);
        Assert.Equal("Limits.regMin", outcome.Expectation.FormerPath);
        Assert.True(outcome.Expectation.Check(SimaticMlInterfaceDocument.Parse(edited).ReadSections()).Success);
    }

    [Fact]
    public void CommentsAreSetPerCulture()
    {
        var update = Update("None", "Scaling.Type");
        update.Comment = "Mode";
        update.CommentCulture = "en-US";
        Assert.Equal(Udt().Replace(">0- multiply, 1 - scale<", ">Mode<"), Apply(Udt(), update, out _));

        var add = Update("None", "Scaling.Type");
        add.Comment = "Modus";
        add.CommentCulture = "de-DE";
        Assert.Equal(Udt().Replace("0- multiply, 1 - scale</MultiLanguageText>",
            "0- multiply, 1 - scale</MultiLanguageText>\n                  <MultiLanguageText Lang=\"de-DE\">Modus</MultiLanguageText>"), Apply(Udt(), add, out _));

        var first = Update("None", "Scaling.regMin");
        first.Comment = "min";
        first.CommentCulture = "en-US";
        Assert.Equal(Udt().Replace("<Member Name=\"regMin\" Datatype=\"Int\">\n                <StartValue>0</StartValue>",
            "<Member Name=\"regMin\" Datatype=\"Int\">\n                <Comment>\n                  <MultiLanguageText Lang=\"en-US\">min</MultiLanguageText>\n                </Comment>\n                <StartValue>0</StartValue>"), Apply(Udt(), first, out _));

        var remove = Update("None", "Scaling.Type");
        remove.Comment = string.Empty;
        remove.CommentCulture = "en-US";
        Assert.Equal(Udt().Replace("\n                <Comment>\n                  <MultiLanguageText Lang=\"en-US\">0- multiply, 1 - scale</MultiLanguageText>\n                </Comment>", string.Empty),
            Apply(Udt(), remove, out var outcome));
        Assert.True(outcome.Expectation.Check(SimaticMlInterfaceDocument.Parse(Apply(Udt(), remove, out _)).ReadSections()).Success);
    }

    [Fact]
    public void StartValuesAreAddedToSelfClosingMembersAndCleared()
    {
        var set = Update("None", "Scaling.valueMin");
        set.StartValue = "0.5";
        Assert.Equal(Udt().Replace("<Member Name=\"valueMin\" Datatype=\"Real\" />",
            "<Member Name=\"valueMin\" Datatype=\"Real\">\n                <StartValue>0.5</StartValue>\n              </Member>"), Apply(Udt(), set, out _));

        var clear = Update("None", "Scaling.Type");
        clear.ClearStartValue = true;
        Assert.Equal(Udt().Replace("\n                <StartValue>TRUE</StartValue>", string.Empty), Apply(Udt(), clear, out var outcome));
        Assert.Equal(("startValue", "TRUE", (string?)null), (outcome.Changes[0].Field, outcome.Changes[0].Current, outcome.Changes[0].Requested));
    }

    [Fact]
    public void CrLfDocumentsKeepTheirLineBreaks()
    {
        var crlf = Udt().Replace("\n", "\r\n");
        var edit = new PlcInterfaceEditRequest { Kind = PlcInterfaceEditKind.Add, Section = "None", Path = PlcMemberPath.Parse("Gain", false), DataType = "Real" };

        var edited = Apply(crlf, edit, out _);

        Assert.Equal(crlf.Replace("            </Member>\r\n          </Section>",
            "            </Member>\r\n            <Member Name=\"Gain\" Datatype=\"Real\" />\r\n          </Section>"), edited);
    }

    [Fact]
    public void CompactGeneratorDocumentsStayCompact()
    {
        var xml = BlockSourceGenerator.Generate("Mixer", "FB", "SCL", obEventClass: null, obNumber: null);
        var edit = new PlcInterfaceEditRequest { Kind = PlcInterfaceEditKind.Add, Section = "Static", Path = PlcMemberPath.Parse("Count", false), DataType = "Int", StartValue = "0" };

        var edited = Apply(xml, edit, out _);

        Assert.Equal(xml.Replace("<Section Name=\"Static\" />", "<Section Name=\"Static\"><Member Name=\"Count\" Datatype=\"Int\"><StartValue>0</StartValue></Member></Section>"), edited);
    }

    [Fact]
    public void QuotedDataTypesAreEscapedAsAttributes()
    {
        var xml = BlockSourceGenerator.Generate("Settings", "GLOBALDB", "DB", obEventClass: null, obNumber: null);
        var edit = new PlcInterfaceEditRequest { Kind = PlcInterfaceEditKind.Add, Section = "Static", Path = PlcMemberPath.Parse("Config", false), DataType = "\"AnalogInputSettings\"" };

        var edited = Apply(xml, edit, out _);

        Assert.Contains("<Member Name=\"Config\" Datatype=\"&quot;AnalogInputSettings&quot;\" />", edited);
        Assert.Equal("\"AnalogInputSettings\"", SimaticMlInterfaceDocument.Parse(edited).ReadSections()[0].Members[0].DataType);
    }

    public static TheoryData<string, string> Refusals() => new()
    {
        { "informative", WorkerFailureCategories.TargetKindUnsupported },
        { "obInput", WorkerFailureCategories.TargetKindUnsupported },
        { "duplicate", WorkerFailureCategories.ValidationError },
        { "missingSection", WorkerFailureCategories.TargetNotFound },
        { "missingMember", WorkerFailureCategories.TargetNotFound },
        { "structStartValue", WorkerFailureCategories.TargetKindUnsupported },
        { "intoScalar", WorkerFailureCategories.TargetKindUnsupported },
        { "badName", WorkerFailureCategories.ValidationError },
        { "noCulture", WorkerFailureCategories.ValidationError },
        { "returnDelete", WorkerFailureCategories.TargetKindUnsupported },
        { "instanceDb", WorkerFailureCategories.TargetKindUnsupported },
        { "safety", WorkerFailureCategories.TargetKindUnsupported },
        { "missingBefore", WorkerFailureCategories.TargetNotFound },
        { "structToScalar", WorkerFailureCategories.TargetKindUnsupported },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void InvalidEditsAreRefusedWithoutChangingTheDocument(string scenario, string category)
    {
        var (xml, edit) = scenario switch
        {
            "informative" => (ObXml(), Set(Update("Input", "Remanence"), e => e.DataType = "Int")),
            "obInput" => (ObXml(), Add("Input", "Extra", "Bool")),
            "duplicate" => (Udt(), Add("None", "Limits.REGMIN", "Int")),
            "missingSection" => (Udt(), Add("Static", "X", "Int")),
            "missingMember" => (Udt(), Set(Update("None", "Limits.nope"), e => e.DataType = "Int")),
            "structStartValue" => (Udt(), Set(Update("None", "Limits"), e => e.StartValue = "0")),
            "intoScalar" => (Udt(), Add("None", "Scaling.Type.X", "Int")),
            "badName" => (Udt(), Add("None", "\"Bad.Name\"", "Int")),
            "noCulture" => (Udt(), Set(Update("None", "Scaling.Type"), e => e.Comment = "x")),
            "returnDelete" => (BlockSourceGenerator.Generate("Calc", "FC", "SCL", null, null), new PlcInterfaceEditRequest { Kind = PlcInterfaceEditKind.Delete, Section = "Return", Path = PlcMemberPath.Parse("Ret_Val", false) }),
            "instanceDb" => (Udt().Replace("SW.Types.PlcStruct", "SW.Blocks.InstanceDB"), Add("None", "X", "Int")),
            "safety" => (ObXml().Replace("<ProgrammingLanguage>LAD</ProgrammingLanguage>\n      <SecondaryType>", "<ProgrammingLanguage>F_LAD</ProgrammingLanguage>\n      <SecondaryType>"), Add("Temp", "X", "Int")),
            "missingBefore" => (Udt(), Set(Add("None", "X", "Int"), e => e.InsertBefore = "nope")),
            "structToScalar" => (Udt(), Set(Update("None", "Limits"), e => e.DataType = "Int")),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        var document = SimaticMlInterfaceDocument.Parse(xml);

        var refusal = Assert.Throws<PlcInterfaceEditException>(() => document.Apply(edit));

        Assert.Equal(category, refusal.Category);
        Assert.Equal(xml, document.Serialize());
    }

    [Fact]
    public void ReturnMemberDataTypeCanChange()
    {
        var xml = BlockSourceGenerator.Generate("Calc", "FC", "SCL", null, null);
        var edit = Set(Update("Return", "Ret_Val"), e => e.DataType = "Int");

        var edited = Apply(xml, edit, out _);

        Assert.Equal(xml.Replace("<Member Name=\"Ret_Val\" Datatype=\"Void\" />", "<Member Name=\"Ret_Val\" Datatype=\"Int\" />"), edited);
    }

    [Fact]
    public void PrepareCopyRenamesAndDropsTheNumber()
    {
        var document = SimaticMlInterfaceDocument.Parse(ObXml());

        document.PrepareCopy("Main_Copy");

        Assert.Equal(ObXml().Replace("<Name>Main</Name>", "<Name>Main_Copy</Name>").Replace("\n      <Number>1</Number>", string.Empty), document.Serialize());
    }

    [Fact]
    public void ExpectationReportsWhatTheReReadShows()
    {
        var edit = Update("None", "Scaling.regMax");
        edit.StartValue = "32000";
        Apply(Udt(), edit, out var outcome);

        var check = outcome.Expectation.Check(SimaticMlInterfaceDocument.Parse(Udt()).ReadSections());

        Assert.False(check.Success);
        Assert.Equal("None.Scaling.regMax startValue=32000", check.Expected);
        Assert.Equal("None.Scaling.regMax dataType=Int startValue=27648", check.Actual);
    }

    private static PlcInterfaceEditRequest Add(string section, string path, string dataType)
        => new() { Kind = PlcInterfaceEditKind.Add, Section = section, Path = PlcMemberPath.Parse(path, false), DataType = dataType };

    private static PlcInterfaceEditRequest Set(PlcInterfaceEditRequest edit, Action<PlcInterfaceEditRequest> change)
    {
        change(edit);
        return edit;
    }
}
