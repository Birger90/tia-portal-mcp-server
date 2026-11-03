using System.Text.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Interface;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

/// <summary>
/// The Siemens-free pieces behind the block-edit review fixes: the recovery store, start value
/// literal equivalence, data type changes, exact rename verification and array narrowing.
/// </summary>
public sealed class PlcBlockEditReviewTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tia-mcp-recovery-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static PlcRecoveryRecord Record(string name) => new()
    {
        Operation = PlcBlockEditMethods.MoveTagTable, ObjectKind = "tagTable", ObjectName = name, PlcName = "PLC_1",
        SourcePath = "PLC_1/" + name, TargetPath = "/Line", Failure = "EngineeringException: import refused",
    };

    [Fact]
    public void RecoveryStore_WritesFixedFileNamesInATimestampedFolderWhateverTheObjectName()
    {
        const string name = "Bad<Name>:With/\\Chars*?|\"";
        var saved = PlcRecoveryStore.Save(Record(name), "<Document />", new[] { (_root, PlcRecoveryStore.LocalAppDataLocation) },
            new DateTime(2026, 11, 1, 2, 3, 4, 5, DateTimeKind.Utc));

        Assert.Null(saved.Error);
        var file = Assert.IsType<PlcRecoveryFileInfo>(saved.File);
        Assert.Equal(_root, Path.GetDirectoryName(file.Directory));
        Assert.Matches(@"^20261101T020304005Z-[0-9a-f]{32}$", Path.GetFileName(file.Directory));
        Assert.Equal(Path.Combine(file.Directory, "document.xml"), file.DocumentPath);
        Assert.Equal(Path.Combine(file.Directory, "recovery.json"), file.MetadataPath);
        Assert.Equal(PlcRecoveryStore.LocalAppDataLocation, file.Location);
        Assert.Equal("<Document />", File.ReadAllText(file.DocumentPath));
        using var metadata = JsonDocument.Parse(File.ReadAllText(file.MetadataPath!));
        Assert.Equal(name, metadata.RootElement.GetProperty("objectName").GetString());
        Assert.Equal("document.xml", metadata.RootElement.GetProperty("documentFile").GetString());
        Assert.Equal("move_tag_table", metadata.RootElement.GetProperty("operation").GetString());
        Assert.Equal(2, Directory.GetFiles(file.Directory).Length);
    }

    [Fact]
    public void RecoveryStore_FallsBackWhenTheFirstRootIsNotWritable()
    {
        Directory.CreateDirectory(_root);
        var blocked = Path.Combine(_root, "blocked");
        File.WriteAllText(blocked, "a file where the recovery root should be");
        var fallback = Path.Combine(_root, "fallback");

        var saved = PlcRecoveryStore.Save(Record("Motors"), "<Document />",
            new[] { (blocked, PlcRecoveryStore.LocalAppDataLocation), (fallback, PlcRecoveryStore.TempLocation) });

        var file = Assert.IsType<PlcRecoveryFileInfo>(saved.File);
        Assert.Equal(PlcRecoveryStore.TempLocation, file.Location);
        Assert.StartsWith(fallback, file.DocumentPath, StringComparison.Ordinal);
        Assert.Contains("localAppData", saved.Error, StringComparison.Ordinal);
        Assert.Contains("Exception", saved.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryStore_ReportsEveryFailureInsteadOfThrowing()
    {
        Directory.CreateDirectory(_root);
        var blocked = Path.Combine(_root, "blocked");
        File.WriteAllText(blocked, "x");

        var saved = PlcRecoveryStore.Save(Record("Motors"), "<Document />",
            new[] { (blocked, PlcRecoveryStore.LocalAppDataLocation), (blocked + "\0invalid", PlcRecoveryStore.TempLocation) });

        Assert.Null(saved.File);
        Assert.Contains("localAppData", saved.Error, StringComparison.Ordinal);
        Assert.Contains("temp", saved.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Int", "5", "+5", true)]
    [InlineData("DInt", "-12", "-12", true)]
    [InlineData("Int", "5", "6", false)]
    [InlineData("Real", "1.5", "1.50", true)]
    [InlineData("LReal", "1.5", "1.5E+00", true)]
    [InlineData("Real", "1.5", "1,5", false)]
    [InlineData("Time", "T#1s", "T#1S", true)]
    [InlineData("Time", "T#1s", "T#1000ms", false)]
    [InlineData("Byte", "16#FF", "255", false)]
    [InlineData("Bool", "TRUE", "true", true)]
    [InlineData(null, "1.5", "1.50", false)]
    [InlineData("\"MyUdt\"", "1", "1.0", false)]
    public void StartValueLiterals_CountOnlyEqualTextOrEqualNumbersOfNumericTypes(string? dataType, string requested, string stored, bool equivalent)
        => Assert.Equal(equivalent, PlcStartValueLiterals.Equivalent(dataType, requested, stored));

    [Fact]
    public void StartValueLiterals_SameNumberIsInvariantAndDecimalOnly()
    {
        Assert.True(PlcStartValueLiterals.SameNumber("1.5", "1.50"));
        Assert.False(PlcStartValueLiterals.SameNumber("1,5", "1.5"));
        Assert.False(PlcStartValueLiterals.SameNumber("T#1s", "T#1s"));
    }

    private static string Udt() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "AnalogInputSettings.xml"));

    private static PlcInterfaceEditRequest Update(string path, Action<PlcInterfaceEditRequest> change)
    {
        var edit = new PlcInterfaceEditRequest { Kind = PlcInterfaceEditKind.Update, Section = "None", Path = PlcMemberPath.Parse(path, allowIndices: false) };
        change(edit);
        return edit;
    }

    [Fact]
    public void DataTypeChange_WithAStartValueNeedsAnExplicitReplacementOrClear()
    {
        var document = SimaticMlInterfaceDocument.Parse(Udt());
        var refused = Assert.Throws<PlcInterfaceEditException>(() => document.Apply(Update("Scaling.regMax", e => e.DataType = "Bool")));
        Assert.Equal(WorkerFailureCategories.ValidationError, refused.Category);
        Assert.Contains("27648", refused.Message, StringComparison.Ordinal);
        Assert.Equal(Udt(), document.Serialize());

        var cleared = SimaticMlInterfaceDocument.Parse(Udt());
        var outcome = cleared.Apply(Update("Scaling.regMax", e => { e.DataType = "Bool"; e.ClearStartValue = true; }));
        Assert.Contains(outcome.Changes, c => c.Field == "startValue" && c.Requested is null);
        var member = cleared.ReadSections()[0].Members[0].Members.Single(m => m.Name == "regMax");
        Assert.Equal(("Bool", null), (member.DataType, member.StartValue));

        var replaced = SimaticMlInterfaceDocument.Parse(Udt());
        replaced.Apply(Update("Scaling.regMax", e => { e.DataType = "DInt"; e.StartValue = "100000"; }));
        member = replaced.ReadSections()[0].Members[0].Members.Single(m => m.Name == "regMax");
        Assert.Equal(("DInt", "100000"), (member.DataType, member.StartValue));
    }

    [Fact]
    public void DataTypeChange_RefusesContentOfTheOldTypeTheModelDoesNotOrder()
    {
        var withSections = Udt().Replace("<Member Name=\"valueMin\" Datatype=\"Real\" />",
            "<Member Name=\"valueMin\" Datatype=\"&quot;FB_Inner&quot;\"><Sections><Section Name=\"Static\" /></Sections></Member>");
        var sections = Assert.Throws<PlcInterfaceEditException>(() =>
            SimaticMlInterfaceDocument.Parse(withSections).Apply(Update("Scaling.valueMin", e => e.DataType = "Real")));
        Assert.Equal(WorkerFailureCategories.TargetKindUnsupported, sections.Category);
        Assert.Contains("Sections", sections.Message, StringComparison.Ordinal);

        var versioned = Udt().Replace("<Member Name=\"valueMax\" Datatype=\"Real\" />", "<Member Name=\"valueMax\" Datatype=\"IEC_TIMER\" Version=\"1.0\" />");
        var version = Assert.Throws<PlcInterfaceEditException>(() =>
            SimaticMlInterfaceDocument.Parse(versioned).Apply(Update("Scaling.valueMax", e => e.DataType = "Real")));
        Assert.Contains("Version", version.Message, StringComparison.Ordinal);

        var subelement = Udt().Replace("<Member Name=\"Multiplier\" Datatype=\"Real\" />",
            "<Member Name=\"Multiplier\" Datatype=\"Array[0..1] of Int\"><Subelement Path=\"0\"><StartValue>1</StartValue></Subelement></Member>");
        Assert.Throws<PlcInterfaceEditException>(() =>
            SimaticMlInterfaceDocument.Parse(subelement).Apply(Update("Scaling.Multiplier", e => e.DataType = "Array[0..3] of Int")));

        // The same data type is no change: nothing is refused.
        SimaticMlInterfaceDocument.Parse(Udt()).Apply(Update("Scaling.regMax", e => e.DataType = "int"));
    }

    [Fact]
    public void CaseOnlyRename_VerifiesTheExactName()
    {
        var document = SimaticMlInterfaceDocument.Parse(Udt());
        var outcome = document.Apply(Update("Scaling.regMax", e => e.NewName = "RegMax"));
        Assert.Equal("RegMax", outcome.Expectation.NewName);

        var renamed = outcome.Expectation.Check(document.ReadSections());
        Assert.True(renamed.Success, renamed.Actual);
        var ignored = outcome.Expectation.Check(SimaticMlInterfaceDocument.Parse(Udt()).ReadSections());
        Assert.False(ignored.Success);
        Assert.Contains("name=regMax", ignored.Actual, StringComparison.Ordinal);
    }

    private static string FailureEvidence(Action<PlcBlockEditFailureInfo>? change = null)
    {
        var info = new PlcBlockEditFailureInfo
        {
            Operation = PlcBlockEditMethods.MoveBlock, ObjectKind = "block", ObjectName = "Motor", SourcePath = "PLC_1/Blocks/Motor",
            TargetPath = "PLC_1/Blocks/Line", OriginalState = PlcBlockEditOriginalStates.Missing,
            Recovery = new PlcRecoveryFileInfo { Directory = "d", DocumentPath = "d/document.xml", MetadataPath = "d/recovery.json", Location = "localAppData" },
            Messages = { "kept" },
        };
        change?.Invoke(info);
        return TiaMcpServer.Contracts.Json.WorkerJson.SerializePayload(info);
    }

    [Fact]
    public void FailedMoveCarriesItsEvidenceAsTheItemResult()
    {
        var failed = WorkerCallResult.Fail(WorkerFailureCategories.WorkerOperationFailed, "move failed") with { PlcDownloadOutcome = FailureEvidence() };

        var item = TiaMcpServer.Plc.PlcPayloadContract.ProjectWrite(
            new TiaMcpServer.Plc.PlcOperationRequest { OperationId = "m", Operation = PlcBlockEditMethods.MoveBlock }, failed);

        Assert.Equal(("failed", "worker_operation_failed"), (item.Status, item.Failure!.Category));
        var result = Assert.IsType<JsonElement>(item.Result);
        Assert.Equal("missing", result.GetProperty("originalState").GetString());
        Assert.Equal("d/document.xml", result.GetProperty("recovery").GetProperty("documentPath").GetString());
    }

    [Fact]
    public void FailureEvidenceIsIgnoredForOtherOperationsAndRejectedWhenMalformed()
    {
        var other = TiaMcpServer.Plc.PlcPayloadContract.ProjectWrite(
            new TiaMcpServer.Plc.PlcOperationRequest { OperationId = "t", Operation = "create_tag" },
            WorkerCallResult.Fail(WorkerFailureCategories.WorkerOperationFailed, "x") with { PlcDownloadOutcome = FailureEvidence() });
        Assert.Null(other.Result);

        var malformed = TiaMcpServer.Plc.PlcPayloadContract.ProjectWrite(
            new TiaMcpServer.Plc.PlcOperationRequest { OperationId = "m", Operation = PlcBlockEditMethods.MoveTagTable },
            WorkerCallResult.Fail(WorkerFailureCategories.WorkerOperationFailed, "x") with { PlcDownloadOutcome = "{\"operation\":\"move_tag_table\"}" });
        Assert.Null(malformed.Result);
        Assert.Equal("worker_operation_failed", malformed.Failure!.Category);
        Assert.Contains("evidence did not match", malformed.Failure.Message, StringComparison.Ordinal);
    }

    private static PlcInterfaceMemberInfo Member(string name, string dataType, params PlcInterfaceMemberInfo[] members)
        => new() { Name = name, Path = name, DataType = dataType, Members = members.ToList() };

    [Fact]
    public void DbValueTree_NarrowedIntoAnArrayEnumeratesOnlyTheRequestedElement()
    {
        var roots = new[] { Member("Big", "Array[0..99999] of Struct", Member("X", "Int"), Member("Y", "Real")) };

        var element = PlcDbValueTree.Build(roots, _ => null, "Big[70000]");
        Assert.True(element.SubtreeFound);
        Assert.Equal(new[] { "Big[70000].X", "Big[70000].Y" }, element.Entries.Select(e => e.Path));

        var leaf = PlcDbValueTree.Build(roots, _ => null, "Big[3].Y");
        Assert.Equal("Big[3].Y", Assert.Single(leaf.Entries).Path);

        var outside = PlcDbValueTree.Build(roots, _ => null, "Big[100000]");
        Assert.False(outside.SubtreeFound);
        Assert.Empty(outside.Entries);

        var matrix = PlcDbValueTree.Build(new[] { Member("M", "Array[1..3, 1..2] of Int") }, _ => null, "M[2,1]");
        Assert.Equal("M[2,1]", Assert.Single(matrix.Entries).Path);
    }
}
